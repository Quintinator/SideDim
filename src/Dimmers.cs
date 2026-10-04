using System.Collections.Concurrent;

namespace SideDim;

/// <param name="Hole">Area left undimmed (the focused window), in screen pixels. Overlay only.</param>
internal sealed record DimTarget(Monitor Monitor, Rectangle? Hole = null);

internal interface IDimmer : IDisposable
{
    /// <summary>Dim exactly these monitors; anything dimmed before and not in the list goes back to normal.</summary>
    void Apply(IReadOnlyList<DimTarget> targets);
}

/// <summary>Black, click-through, topmost window over each monitor. Works on every display. UI thread only.</summary>
internal sealed class OverlayDimmer(Func<Settings> settings) : IDimmer
{
    private readonly Dictionary<string, OverlayForm> _overlays = [];
    private readonly HashSet<OverlayForm> _fadingOut = [];

    public void Apply(IReadOnlyList<DimTarget> targets)
    {
        var s = settings();
        var opacity = Math.Clamp(s.OverlayStrength, 0, Settings.MaxOverlayStrength) / 100.0;
        var wanted = targets.Select(t => t.Monitor.Device).ToHashSet();

        foreach (var (device, form) in _overlays.ToList())
        {
            if (wanted.Contains(device)) continue;
            _overlays.Remove(device);
            _fadingOut.Add(form);
            form.FormClosed += (_, _) => _fadingOut.Remove(form);
            form.FadeTo(0, s.FadeMs, closeWhenDone: true);
        }

        foreach (var t in targets)
        {
            if (!_overlays.TryGetValue(t.Monitor.Device, out var form))
            {
                form = new OverlayForm(t.Monitor.Bounds);
                _overlays[t.Monitor.Device] = form;
                form.Show();
            }
            form.Place(t.Monitor.Bounds);
            form.SetHole(t.Hole is { } h ? DimPolicy.ToOverlayCoordinates(h, t.Monitor.Bounds) : null);
            form.FadeTo(opacity, s.FadeMs, closeWhenDone: false);
        }
    }

    public void Dispose()
    {
        foreach (var form in _overlays.Values.Concat(_fadingOut).ToList()) form.Close();
        _overlays.Clear();
        _fadingOut.Clear();
    }

    private sealed class OverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private double _from, _to = -1;
        private int _duration;
        private bool _closeWhenDone;
        private Rectangle? _hole;

        public OverlayForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Bounds = bounds;
            // No TopMost = true here: WinForms applies it with a SetWindowPos that activates the window,
            // which would steal focus from the game. WS_EX_TOPMOST plus Place() does it without activating.
            Opacity = 0;
            _timer.Tick += (_, _) => Step();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                            | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Bounds are already in physical pixels; don't let WinForms rescale when crossing DPI boundaries.
            if (m.Msg == Native.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        public void Place(Rectangle b)
        {
            if (!Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE))
                Log.Write($"Could not position overlay at {b} (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        }

        /// <param name="hole">In overlay coordinates, or null to cover the whole monitor.</param>
        public void SetHole(Rectangle? hole)
        {
            if (hole == _hole) return;
            _hole = hole;
            var old = Region;
            if (hole is { } h)
            {
                var region = new Region(new Rectangle(Point.Empty, Size));
                region.Exclude(h);
                Region = region;
            }
            else
            {
                Region = null;
            }
            old?.Dispose();
        }

        public void FadeTo(double target, int durationMs, bool closeWhenDone)
        {
            if (Math.Abs(target - _to) < 0.001 && closeWhenDone == _closeWhenDone) return;
            _from = Opacity;
            _to = target;
            _duration = Math.Max(durationMs, 1);
            _closeWhenDone = closeWhenDone;
            _clock.Restart();
            _timer.Start();
            Step();
        }

        private void Step()
        {
            var t = Math.Min(_clock.Elapsed.TotalMilliseconds / _duration, 1.0);
            var eased = 1 - Math.Pow(1 - t, 3);
            Opacity = _from + (_to - _from) * eased;
            if (t < 1) return;
            _timer.Stop();
            if (_closeWhenDone) Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                Region?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// Runs a <see cref="BacklightController"/> on its own thread: DDC/CI calls take tens of milliseconds
/// each and must never block the UI thread.
/// </summary>
internal sealed class HardwareDimmer : IDimmer
{
    private readonly BacklightController _controller;
    private readonly Func<int> _level;
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _worker;
    private int _disposed;

    public HardwareDimmer(BacklightController controller, Func<int> level)
    {
        _controller = controller;
        _level = level;
        _worker = new Thread(Work) { IsBackground = true, Name = "DDC/CI" };
        _worker.Start();

        if (controller.Originals.Count > 0)
        {
            Log.Write($"Restoring brightness left over from last session: {string.Join(", ", controller.Originals.Keys)}");
            _queue.Add(controller.RestoreAll);
        }
    }

    public void Apply(IReadOnlyList<DimTarget> targets)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var devices = targets.Select(t => t.Monitor.Device).ToList();
        var level = (uint)Math.Clamp(_level(), 0, 100);
        _queue.Add(() => _controller.Apply(devices, level));
    }

    /// <summary>Blocks until everything queued so far has run (or the timeout passes).</summary>
    public bool Flush(TimeSpan? timeout = null)
    {
        if (Volatile.Read(ref _disposed) != 0) return true;
        using var done = new ManualResetEventSlim();
        _queue.Add(done.Set);
        return done.Wait(timeout ?? TimeSpan.FromSeconds(5));
    }

    private void Work()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
        {
            try { job(); }
            catch (Exception e) { Log.Write($"DDC/CI error: {e}"); }
        }
    }

    /// <summary>Restores every monitor, then stops the worker. Safe to call from any thread, more than once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Add(_controller.RestoreAll);
        _queue.CompleteAdding();
        if (!_worker.Join(TimeSpan.FromSeconds(5)))
            Log.Write("Gave up waiting for monitors to restore; hardware-state.json will finish the job on next start.");
        _queue.Dispose();
    }
}
