using System.Collections.Concurrent;

namespace SideDim;

/// <param name="Hole">In screen pixels, not overlay coordinates; only the overlay dimmer uses it.</param>
internal sealed record DimTarget(Monitor Monitor, int OverlayStrength, int BacklightLevel, Rectangle? Hole = null);

internal interface IDimmer : IDisposable
{
    /// <summary>Full state, not a delta: a monitor dimmed before and missing from the list must go back to normal.</summary>
    void Apply(IReadOnlyList<DimTarget> targets);
}

/// <summary>UI thread only.</summary>
internal sealed class OverlayDimmer(Func<Settings> settings) : IDimmer
{
    private readonly Dictionary<string, OverlayForm> _overlays = [];
    private readonly HashSet<OverlayForm> _fadingOut = [];

    public void Apply(IReadOnlyList<DimTarget> targets)
    {
        var s = settings();
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
            form.FadeTo(Math.Clamp(t.OverlayStrength, 0, Settings.MaxOverlayStrength) / 100.0, s.FadeMs, closeWhenDone: false);
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
        private Size _regionSize;

        /// <remarks>Never set TopMost: WinForms applies it with an activating SetWindowPos that steals focus; WS_EX_TOPMOST plus Place keeps it on top instead.</remarks>
        public OverlayForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Bounds = bounds;
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

        /// <remarks>Ignores WM_DPICHANGED: bounds are already physical pixels, so WinForms must not rescale them on a DPI boundary.</remarks>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        public void Place(Rectangle b)
        {
            if (!Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE))
                Log.Write($"Could not position overlay at {b} (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        }

        public void SetHole(Rectangle? hole)
        {
            if (hole == _hole && (hole is null || Size == _regionSize)) return;
            _hole = hole;
            _regionSize = Size;
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

/// <summary>DDC/CI calls take tens of milliseconds each, so they run on a worker thread and must never block the UI thread.</summary>
internal sealed class HardwareDimmer : IDimmer
{
    private readonly BacklightController _controller;
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _worker;
    private int _disposed;

    public HardwareDimmer(BacklightController controller)
    {
        _controller = controller;
        _worker = new Thread(Work) { IsBackground = true, Name = "DDC/CI" };
        _worker.Start();

        if (controller.Originals.Count > 0)
        {
            Log.Write($"Restoring brightness left over from last session: {string.Join(", ", controller.Originals.Keys)}");
            _queue.Add(controller.RestoreAll);
        }
    }

    /// <remarks>Keyed by Monitor.Id, not Device, because Windows can renumber devices.</remarks>
    public void Apply(IReadOnlyList<DimTarget> targets)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var levels = targets.ToDictionary(t => t.Monitor.Id, t => (uint)Math.Clamp(t.BacklightLevel, 0, 100), StringComparer.OrdinalIgnoreCase);
        _queue.Add(() => _controller.Apply(levels));
    }

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

    /// <remarks>Safe from any thread, more than once. Dispose the queue only after the worker stopped; the join stays capped so a hung DDC/CI bus can't block exit.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Add(_controller.RestoreAll);
        _queue.CompleteAdding();
        if (_worker.Join(TimeSpan.FromSeconds(5))) _queue.Dispose();
        else Log.Write("Gave up waiting for monitors to restore; hardware-state.json will finish the job on next start.");
    }
}
