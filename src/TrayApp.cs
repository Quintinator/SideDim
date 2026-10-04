using System.Diagnostics;
using Microsoft.Win32;

namespace SideDim;

/// <summary>What the settings window needs from the app.</summary>
internal interface ISettingsHost
{
    /// <summary>Saves Settings and re-applies dimming. Called after the settings window or tray menu changed Settings.</summary>
    void SaveAndApplySettings();

    /// <summary>The Test button: "Dim now" around whatever window has focus, i.e. the settings window itself.</summary>
    void ToggleTest();

    /// <summary>"Dim now" is on (from the hotkey, the tray menu or the Test button).</summary>
    bool IsDimNowOn { get; }

    event Action? DimNowChanged;

    /// <summary>Stops the global hotkey while the user records a new one; registers the new one when resumed.</summary>
    void SuspendHotkey(bool suspend);

    /// <summary>False when the hotkey is invalid or another app already owns it.</summary>
    bool HotkeyWorks { get; }

    /// <summary>Connected monitors. With one, backlight dimming is unavailable and the spotlight is always on.</summary>
    int MonitorCount { get; }

    event Action? DisplaysChanged;
}

internal sealed class TrayApp : ApplicationContext, ISettingsHost
{
    private static readonly TimeSpan ActivePoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PausedPoll = TimeSpan.FromSeconds(1); // only keeps "Dim for <last app>" current

    // DDC/CI often ignores a monitor for the first seconds after it is plugged in or woken up.
    private static readonly TimeSpan DisplaySettleDelay = TimeSpan.FromMilliseconds(2500);

    private readonly SynchronizationContext _ui;
    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly Icon _trayIcon;
    private readonly TrayMenu _menu;
    private readonly System.Windows.Forms.Timer _poll = new();
    private readonly System.Windows.Forms.Timer _displaySettled = new() { Interval = (int)DisplaySettleDelay.TotalMilliseconds };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HotkeyWindow _hotkey = new();
    private readonly FocusDebouncer _debouncer = new();
    private readonly OverlayDimmer _overlay;
    private readonly OverlayDimmer _spotlight;
    private readonly HardwareDimmer _backlight;
    private SettingsForm? _form;

    private string? _dimNow;              // "Dim now" (hotkey, tray or Test button): the monitor kept bright
    private string? _dimmedFor;           // monitor kept bright by the last ApplyDimming, for logging
    private string? _lastExternalMonitor; // where the last real app window was, for "Dim now" from the tray
    private readonly HashSet<string> _warnedNoDdc = []; // monitors the user was told about (UI thread only)
    private Rectangle? _appliedHole;
    private bool _spotlightDirty;
    private bool _hotkeySuspended;
    private int _monitorCount;

    public string LastApp { get; private set; } = "";
    public string? LastAppPath { get; private set; }
    public bool IsDimNowOn => _dimNow is not null;
    public bool HotkeyWorks { get; private set; }
    public int MonitorCount => _monitorCount;
    public event Action? DimNowChanged;
    public event Action? DisplaysChanged;

    public TrayApp(SynchronizationContext ui)
    {
        _ui = ui;
        _settings = Settings.Load();
        Autostart.RepairPath();

        _overlay = new OverlayDimmer(() => _settings);
        _spotlight = new OverlayDimmer(() => _settings);
        // Created even in Overlay mode: it restores brightness a crashed previous run left dimmed.
        _backlight = new HardwareDimmer(
            new BacklightController(
                new DdcBrightnessDevice(),
                new FileBrightnessStore(AppPaths.BrightnessStateFile, legacyPath: Path.Combine(AppPaths.Folder, "hardware-state.json")),
                OnNoDdc));

        _trayIcon = AppIcon.Load(SystemInformation.SmallIconSize);
        _menu = new TrayMenu(this);
        _tray = new NotifyIcon { Icon = _trayIcon, Visible = true, ContextMenuStrip = _menu.Strip };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        // The hotkey may count our settings window as the focused app: the user is looking at it when they press it.
        _hotkey.Pressed += () => ToggleDimNowCore(includeOwnWindows: true);
        RegisterHotkey();

        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        SystemEvents.SessionEnded += OnSessionEnded;
        _displaySettled.Tick += (_, _) =>
        {
            _displaySettled.Stop();
            ApplyDimming(); // second pass for monitors that weren't answering DDC/CI yet
        };

        _monitorCount = Monitors.All().Count;
        _poll.Tick += (_, _) => Tick();
        UpdatePollRate();
        _poll.Start();
        UpdateTooltip();
        Log.Write($"Started {Application.ProductVersion}, mode {_settings.Mode}, trigger {_settings.Trigger}, {_monitorCount} monitor(s)");

        if (_settings.IsFirstRun) ShowSettings();
        else if (!HotkeyWorks)
            _tray.ShowBalloonTip(5000, "SideDim", $"The hotkey {_settings.ToggleHotkey} is taken by another app. Pick another one in the settings.", ToolTipIcon.Warning);
    }

    internal Settings Settings => _settings;

    private void Tick()
    {
        // While testing, our own settings window stands in for the focused app.
        var fg = ForegroundWatcher.Current(includeOwnWindows: IsDimNowOn);
        if (fg is not null && fg.ProcessId != Environment.ProcessId)
        {
            _lastExternalMonitor = fg.Monitor.Device;
            if (fg.Process.Length > 0 && !_settings.IsNeverDim(fg.Process))
            {
                LastApp = fg.Process;
                LastAppPath = fg.Path;
            }
        }

        // "Dim now" follows the window to another monitor, the same way automatic dimming does.
        if (_dimNow is not null && fg is not null && fg.Monitor.Device != _dimNow) _dimNow = fg.Monitor.Device;

        var desired = _dimNow
            ?? (_settings.Enabled && fg is not null && DimPolicy.ShouldDim(_settings, fg.Process, fg.IsFullscreen) ? fg.Monitor.Device : null);

        var dimDelay = TimeSpan.FromMilliseconds(_settings.DimDelayMs);
        var restoreDelay = TimeSpan.FromMilliseconds(_settings.RestoreDelayMs);
        if (_debouncer.Update(desired, _clock.Elapsed, dimDelay, restoreDelay)) ApplyDimming();

        UpdateSpotlight(fg);
    }

    /// <summary>
    /// Dims every monitor except the one the debouncer says to keep bright. With nothing dimmed this
    /// still runs the backlight restore, which retries monitors that were unplugged while dimmed.
    /// </summary>
    private void ApplyDimming()
    {
        var monitors = Monitors.All();
        _monitorCount = monitors.Count;
        var keep = _debouncer.Applied;
        if (keep is not null && monitors.All(m => m.Device != keep))
        {
            // The kept monitor was unplugged; dimming "all the others" would dim everything.
            keep = null;
            _dimNow = null;
            _debouncer.ForceApply(null);
            DimNowChanged?.Invoke();
        }

        var others = DimPolicy.Targets(_settings, monitors, keep);
        _backlight.Apply(_settings.UsesBacklight ? others : []);
        _overlay.Apply(_settings.UsesOverlay ? others : []);

        if (keep != _dimmedFor) Log.Write(keep is null ? "Restored" : $"Dimmed all but {keep} ({(_dimNow is not null ? "Dim now" : LastApp)})");
        _dimmedFor = keep;
        _spotlightDirty = true;
        UpdateTooltip();
    }

    /// <summary>Darkens the focused screen around the focused window. Follows the window as it moves.</summary>
    private void UpdateSpotlight(Foreground? fg)
    {
        // A click on the taskbar or desktop shouldn't flash the spotlight off; it ends when dimming ends.
        if (fg is null && _debouncer.Applied is not null && !_spotlightDirty) return;

        var levels = fg is null ? default : _settings.LevelsFor(fg.Monitor.Id);
        var on = DimPolicy.SpotlightOn(_settings, _monitorCount) && levels.Dim; // a "never dim" screen gets no spotlight either
        var hole = fg is null ? null
            : DimPolicy.SpotlightHole(on, _debouncer.Applied, fg.Monitor.Device, fg.Bounds, fg.IsFullscreen);

        if (hole == _appliedHole && !_spotlightDirty) return;
        _appliedHole = hole;
        _spotlightDirty = false;
        _spotlight.Apply(hole is null || fg is null ? [] : [new DimTarget(fg.Monitor, levels.OverlayStrength, levels.BacklightLevel, hole)]);
    }

    /// <summary>The settings window's Test button: the settings window itself stands in for the focused app.</summary>
    public void ToggleTest() => ToggleDimNowCore(includeOwnWindows: true);

    /// <summary>"Dim now" from the tray menu: keeps the monitor of the app you were using bright.</summary>
    public void ToggleDimNow() => ToggleDimNowCore(includeOwnWindows: false);

    private void ToggleDimNowCore(bool includeOwnWindows)
    {
        if (_dimNow is not null)
        {
            // Hand back to the automatic rule rather than forcing a restore: if the app still has focus
            // the screens just stay dim, instead of flashing bright and dimming again. With automatic
            // dimming paused there's nothing to hand back to, so restore right away.
            _dimNow = null;
            if (!_settings.Enabled)
            {
                _debouncer.ForceApply(null);
                ApplyDimming();
                UpdateSpotlight(null);
            }
        }
        else
        {
            // From the tray the foreground is our own (invisible) menu window, so fall back to the last real app.
            _dimNow = ForegroundWatcher.Current(includeOwnWindows)?.Monitor.Device
                ?? _lastExternalMonitor
                ?? Screen.FromPoint(Cursor.Position).DeviceName;
            _debouncer.ForceApply(_dimNow);
            ApplyDimming();
        }
        UpdatePollRate();
        DimNowChanged?.Invoke();
    }

    public void SaveAndApplySettings()
    {
        _settings.Normalize().Save();
        if (!_settings.Enabled && _dimNow is null && _debouncer.Applied is not null) _debouncer.ForceApply(null);
        ApplyDimming(); // picks up mode and strength changes
        UpdatePollRate();
    }

    public void SuspendHotkey(bool suspend)
    {
        _hotkeySuspended = suspend;
        if (suspend) _hotkey.Unregister();
        else RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        if (_hotkeySuspended) return; // the user is recording a new one in the settings window
        HotkeyWorks = Hotkey.TryParse(_settings.ToggleHotkey, out var hk) && _hotkey.Register(hk);
        if (!HotkeyWorks) Log.Write($"Could not register hotkey '{_settings.ToggleHotkey}' (invalid or taken by another app)");
    }

    private void UpdatePollRate() =>
        _poll.Interval = (int)(_settings.Enabled || IsDimNowOn ? ActivePoll : PausedPoll).TotalMilliseconds;

    public void ShowSettings()
    {
        if (_form is not null)
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
        SaveAndApplySettings();
        _form?.ReloadApps();
    }

    public void ToggleEnabled()
    {
        _settings.Enabled = !_settings.Enabled;
        SaveAndApplySettings();
        _form?.ReloadValues();
    }

    public void ToggleAutostart()
    {
        if (!Autostart.TrySet(!Autostart.IsOn))
            _tray.ShowBalloonTip(5000, "SideDim", "Windows did not allow changing Start with Windows. See the log for details.", ToolTipIcon.Warning);
        _form?.ReloadValues();
    }

    /// <summary>Called on the DDC/CI thread whenever a monitor doesn't answer; tells the user once, when it matters.</summary>
    private void OnNoDdc(string monitor) => _ui.Post(_ =>
    {
        if (_settings.Mode != DimMode.Hardware) return; // in Both mode the overlay still dims it
        if (_displaySettled.Enabled) return;          // just plugged in or woken up: the settled pass decides
        if (!_warnedNoDdc.Add(monitor)) return;
        _tray.ShowBalloonTip(8000, "SideDim",
            "A monitor doesn't answer DDC/CI, so its backlight can't be dimmed. Turn on DDC/CI in the monitor's own menu, or switch to Overlay or Both.",
            ToolTipIcon.Warning);
    }, null);

    private void UpdateTooltip()
    {
        var state = _debouncer.Applied is not null ? "dimming" : _settings.Enabled ? "watching" : "paused";
        _tray.Text = $"SideDim: {state}";
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) => _ui.Post(_ =>
    {
        // Re-place overlays, re-resolve DDC handles, recount monitors, and retry restoring monitors
        // that came back. Then once more after the layout settles, for monitors that wake up slowly.
        _displaySettled.Stop();
        _displaySettled.Start();
        ApplyDimming();
        DisplaysChanged?.Invoke();
    }, null);

    /// <summary>Logoff, restart or shutdown: clean up fully (restoring brightness) before Windows ends the process.</summary>
    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        Log.Write($"Session ended ({e.Reason})");
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _poll.Stop();
        _displaySettled.Stop();
        _tray.Visible = false; // disappear right away; the restore below can take a moment
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        SystemEvents.SessionEnded -= OnSessionEnded;
        _form?.Close();
        _spotlight.Dispose();
        _overlay.Dispose();
        _backlight.Dispose(); // waits (up to 5 s) until every monitor has its brightness back
        _hotkey.Dispose();
        _tray.Dispose();
        _menu.Dispose();
        _trayIcon.Dispose();
        _poll.Dispose();
        _displaySettled.Dispose();
        Log.Write("Exited");
        base.ExitThreadCore();
    }

    /// <summary>Last-ditch restore from the crash handler. Only touches thread-safe parts.</summary>
    public void EmergencyRestore()
    {
        try
        {
            _backlight.Dispose();
        }
        catch (Exception e)
        {
            Log.Write($"Emergency restore failed: {e.Message}");
        }

        try
        {
            _tray.Visible = false; // no ghost icon left in the tray
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Best effort only.
        }
    }

    /// <summary>The right-click menu. Built once; checkmarks and labels refresh each time it opens.</summary>
    private sealed class TrayMenu : IDisposable
    {
        private readonly Font _bold;
        private readonly ToolStripMenuItem _enabled, _dimNow, _lastApp, _autostart;

        public ContextMenuStrip Strip { get; } = new();

        public TrayMenu(TrayApp app)
        {
            var open = new ToolStripMenuItem("Settings…");
            _bold = new Font(open.Font, FontStyle.Bold);
            open.Font = _bold;
            open.Click += (_, _) => app.ShowSettings();

            _enabled = Item("Automatic dimming", app.ToggleEnabled);
            _dimNow = Item("Dim now", app.ToggleDimNow);
            _lastApp = Item("", app.ToggleLastApp);
            _autostart = Item("Start with Windows", app.ToggleAutostart);

            Strip.Items.AddRange([
                open, new ToolStripSeparator(),
                _enabled, _dimNow, _lastApp, new ToolStripSeparator(),
                _autostart, Item("Exit", app.ExitThread),
            ]);

            Strip.Opening += (_, _) =>
            {
                var s = app.Settings;
                _enabled.Checked = s.Enabled;
                _dimNow.Checked = app.IsDimNowOn;
                _dimNow.ShortcutKeyDisplayString = app.HotkeyWorks ? s.ToggleHotkey : $"{s.ToggleHotkey} (not working)";
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
