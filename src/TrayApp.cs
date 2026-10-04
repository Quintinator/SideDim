using System.Diagnostics;
using Microsoft.Win32;

namespace SideDim;

/// <summary>What the settings window needs from the app.</summary>
internal interface ISettingsHost
{
    /// <summary>Save and apply after any change.</summary>
    void SettingsChanged();

    /// <summary>The test button: dim around whatever window has focus (the settings window itself).</summary>
    void ToggleTest();

    bool IsTesting { get; }

    event Action? TestingChanged;

    /// <summary>Stops the global hotkey while the user is typing a new one.</summary>
    void SuspendHotkey(bool suspend);

    /// <summary>False when the hotkey is invalid or another app already owns it.</summary>
    bool HotkeyWorks { get; }
}

internal sealed class TrayApp : ApplicationContext, ISettingsHost
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly string OwnName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "SideDim");

    private readonly SynchronizationContext _ui;
    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly Icon _trayIcon;
    private readonly TrayMenu _menu;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = (int)PollInterval.TotalMilliseconds };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HotkeyWindow _hotkey = new();
    private readonly FocusDebouncer _debouncer = new();
    private readonly OverlayDimmer _overlay;
    private readonly OverlayDimmer _spotlight;
    private readonly HardwareDimmer _backlight;
    private SettingsForm? _form;

    private string? _manual;      // monitor kept bright by the hotkey / test button
    private string? _dimmedFor;   // monitor kept bright by the last ApplyDimming, for logging
    private Rectangle? _appliedHole;
    private bool _spotlightDirty;

    public string LastApp { get; private set; } = "";
    public string? LastAppPath { get; private set; }
    public bool IsTesting => _manual is not null;
    public bool HotkeyWorks { get; private set; }
    public event Action? TestingChanged;

    public TrayApp(SynchronizationContext ui)
    {
        _ui = ui;
        _settings = Settings.Load();
        Autostart.RepairPath();

        _overlay = new OverlayDimmer(() => _settings);
        _spotlight = new OverlayDimmer(() => _settings);
        // Created even in Overlay mode: it restores brightness a crashed previous run left dimmed.
        _backlight = new HardwareDimmer(
            new BacklightController(new DdcBrightnessDevice(), new FileBrightnessStore(AppPaths.BrightnessStateFile)),
            () => _settings.BacklightLevel);

        _trayIcon = AppIcon.Load(SystemInformation.SmallIconSize);
        _menu = new TrayMenu(this);
        _tray = new NotifyIcon { Icon = _trayIcon, Visible = true, ContextMenuStrip = _menu.Strip };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        _hotkey.Pressed += ToggleTest;
        RegisterHotkey();

        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        SystemEvents.SessionEnding += OnSessionEnding;

        _poll.Tick += (_, _) => Tick();
        _poll.Start();
        UpdateTooltip();
        Log.Write($"Started {Application.ProductVersion}, mode {_settings.Mode}, trigger {_settings.Trigger}");

        if (_settings.IsFirstRun) ShowSettings();
    }

    internal Settings Settings => _settings;

    private void Tick()
    {
        // While testing, our own settings window stands in for the game.
        var fg = ForegroundWatcher.Current(includeOwnWindows: IsTesting);
        if (fg is { Process.Length: > 0 } && fg.Process != OwnName)
        {
            LastApp = fg.Process;
            LastAppPath = fg.Path;
        }

        var desired = _manual
            ?? (_settings.Enabled && fg is not null && DimPolicy.ShouldDim(_settings, fg.Process, fg.IsFullscreen) ? fg.Monitor.Device : null);

        var dimDelay = TimeSpan.FromMilliseconds(_settings.DimDelayMs);
        var restoreDelay = TimeSpan.FromMilliseconds(_settings.RestoreDelayMs);
        if (_debouncer.Update(desired, _clock.Elapsed, dimDelay, restoreDelay)) ApplyDimming();

        UpdateSpotlight(fg);
    }

    /// <summary>Dims every monitor except the one the debouncer says to keep bright.</summary>
    private void ApplyDimming()
    {
        var monitors = Monitors.All();
        var keep = _debouncer.Applied;
        if (keep is not null && monitors.All(m => m.Device != keep))
        {
            // The kept monitor was unplugged; dimming "all the others" would dim everything.
            keep = null;
            _manual = null;
            _debouncer.ForceApply(null);
            TestingChanged?.Invoke();
        }

        List<DimTarget> others = keep is null ? [] : monitors.Where(m => m.Device != keep).Select(m => new DimTarget(m)).ToList();
        _backlight.Apply(_settings.UsesBacklight ? others : []);
        _overlay.Apply(_settings.UsesOverlay ? others : []);

        if (keep != _dimmedFor) Log.Write(keep is null ? "Restored" : $"Dimmed all but {keep} ({LastApp})");
        _dimmedFor = keep;
        _spotlightDirty = true;
        UpdateTooltip();
    }

    /// <summary>Darkens the focused screen around the focused window. Follows the window as it moves.</summary>
    private void UpdateSpotlight(Foreground? fg)
    {
        var hole = fg is null ? null
            : DimPolicy.SpotlightHole(_settings, _debouncer.Applied, fg.Monitor.Device, fg.Bounds, fg.IsFullscreen);

        if (hole == _appliedHole && !_spotlightDirty) return;
        _appliedHole = hole;
        _spotlightDirty = false;
        _spotlight.Apply(hole is null || fg is null ? [] : [new DimTarget(fg.Monitor, hole)]);
    }

    public void ToggleTest()
    {
        _manual = _manual is null
            ? ForegroundWatcher.Current(includeOwnWindows: true)?.Monitor.Device ?? Screen.FromPoint(Cursor.Position).DeviceName
            : null;
        _debouncer.ForceApply(_manual);
        ApplyDimming();
        TestingChanged?.Invoke();
    }

    public void SettingsChanged()
    {
        _settings.Normalize().Save();
        RegisterHotkey();

        if (!_settings.Enabled && _manual is null && _debouncer.Applied is not null) _debouncer.ForceApply(null);
        ApplyDimming(); // picks up mode and strength changes
        UpdateTooltip();
    }

    public void SuspendHotkey(bool suspend)
    {
        if (suspend) _hotkey.Unregister();
        else RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        HotkeyWorks = Hotkey.TryParse(_settings.ToggleHotkey, out var hk) && _hotkey.Register(hk);
        if (!HotkeyWorks) Log.Write($"Could not register hotkey '{_settings.ToggleHotkey}' (invalid or taken by another app)");
    }

    public void ShowSettings()
    {
        if (_form is { IsDisposed: false })
        {
            _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            return;
        }
        _form = new SettingsForm(_settings, this);
        _form.FormClosed += (_, _) => _form = null;
        _form.Show();
        _form.Activate();
    }

    /// <summary>Adds or removes the last focused app from the list (tray menu shortcut).</summary>
    public void ToggleLastApp()
    {
        if (LastApp.Length == 0) return;
        if (!_settings.RemoveApp(LastApp)) _settings.AddApp(LastAppPath ?? LastApp);
        SettingsChanged();
        _form?.ReloadApps();
    }

    public void ToggleEnabled()
    {
        _settings.Enabled = !_settings.Enabled;
        SettingsChanged();
        _form?.ReloadValues();
    }

    public void ToggleAutostart()
    {
        Autostart.IsOn = !Autostart.IsOn;
        _form?.ReloadValues();
    }

    private void UpdateTooltip()
    {
        var state = _debouncer.Applied is not null ? "dimming" : _settings.Enabled ? "watching" : "paused";
        _tray.Text = $"SideDim: {state}";
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => _ui.Post(_ =>
    {
        // Monitor layout changed: re-place overlays and re-resolve DDC handles.
        if (_debouncer.Applied is not null) ApplyDimming();
    }, null);

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        // Windows may kill us right after this returns, so restore synchronously (the worker is thread-safe).
        _backlight.Apply([]);
        _backlight.Flush(TimeSpan.FromSeconds(3));
        _ui.Post(_ => _debouncer.ForceApply(null), null);
    }

    protected override void ExitThreadCore()
    {
        _poll.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _form?.Close();
        _spotlight.Dispose();
        _overlay.Dispose();
        _backlight.Dispose(); // waits until every monitor has its brightness back
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _trayIcon.Dispose();
        _poll.Dispose();
        Log.Write("Exited");
        base.ExitThreadCore();
    }

    /// <summary>Last-ditch restore from the crash handler. Only touches the thread-safe backlight worker.</summary>
    public void EmergencyRestore()
    {
        try { _backlight.Dispose(); }
        catch (Exception e) { Log.Write($"Emergency restore failed: {e.Message}"); }
    }

    /// <summary>The right-click menu. Built once; checkmarks and labels refresh each time it opens.</summary>
    private sealed class TrayMenu : IDisposable
    {
        private readonly Font _bold;
        private readonly ToolStripMenuItem _enabled, _test, _lastApp, _autostart;

        public ContextMenuStrip Strip { get; } = new();

        public TrayMenu(TrayApp app)
        {
            var open = new ToolStripMenuItem("Settings…");
            _bold = new Font(open.Font, FontStyle.Bold);
            open.Font = _bold;
            open.Click += (_, _) => app.ShowSettings();

            _enabled = Item("Automatic dimming", app.ToggleEnabled);
            _test = Item("Dim now", app.ToggleTest);
            _lastApp = Item("", app.ToggleLastApp);
            _autostart = Item("Start with Windows", app.ToggleAutostart);

            Strip.Items.AddRange([
                open, new ToolStripSeparator(),
                _enabled, _test, _lastApp, new ToolStripSeparator(),
                _autostart, Item("Exit", app.ExitThread),
            ]);

            Strip.Opening += (_, _) =>
            {
                var s = app.Settings;
                _enabled.Checked = s.Enabled;
                _test.Checked = app.IsTesting;
                _test.ShortcutKeyDisplayString = s.ToggleHotkey;
                _lastApp.Visible = app.LastApp.Length > 0 && s.Trigger == DimTrigger.SelectedApps;
                _lastApp.Text = $"Dim for {app.LastApp}";
                _lastApp.Checked = Settings.ListContains(s.Apps, app.LastApp);
                _autostart.Checked = Autostart.IsOn;
            };
        }

        private static ToolStripMenuItem Item(string text, Action onClick)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) => onClick();
            return item;
        }

        public void Dispose()
        {
            Strip.Dispose(); // disposes its items too
            _bold.Dispose();
        }
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int Id = 1;
    private const uint MOD_NOREPEAT = 0x4000;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _registered = Native.RegisterHotKey(Handle, Id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key);
        if (!_registered) Log.Write($"RegisterHotKey({hotkey}) failed, error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) Native.UnregisterHotKey(Handle, Id);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}

internal static class AppIcon
{
    /// <summary>The embedded multi-size icon, at the size closest to <paramref name="size"/>.</summary>
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SideDim.ico")
            ?? throw new InvalidOperationException("SideDim.ico is missing from the assembly resources");
        return new Icon(stream, size);
    }
}
