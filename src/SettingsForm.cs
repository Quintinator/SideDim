using System.ComponentModel;
using System.Diagnostics;

namespace SideDim;

internal sealed class SettingsForm : Form
{
    private static readonly TimeSpan SliderSettle = TimeSpan.FromMilliseconds(250);

    private readonly Settings _s;
    private readonly ISettingsHost _host;
    private readonly IContainer _components = new Container();
    private readonly System.Windows.Forms.Timer _sliderCommit;
    /// <summary><see cref="SystemFonts.MessageBoxFont"/> returns a new Font on every call, so this form owns and disposes it.</summary>
    private readonly Font? _uiFont = SystemFonts.MessageBoxFont;
    private readonly Font _headerFont;
    private readonly Icon _windowIcon;
    private readonly List<Image> _appImages = [];
    private List<ScreenInfo> _screens = [];

    private readonly CheckBox _enabled = new() { Text = "SideDim is on", AutoSize = true };
    private readonly RadioButton _triggerApps = new() { Text = "When one of these apps is focused", AutoSize = true };
    private readonly RadioButton _triggerAny = new() { Text = "Always, for whatever window is focused", AutoSize = true };
    private readonly CheckBox _alsoFullscreen = new() { Text = "Also when any other app goes fullscreen", AutoSize = true };
    private readonly ListView _apps = new()
    {
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        MultiSelect = true,
        HideSelection = false,
    };
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit };
    private readonly Button _addExe = new() { Text = "Browse for .exe…", AutoSize = true };
    private readonly Button _addRunning = new() { Text = "Pick running app ▾", AutoSize = true };
    private readonly Button _remove = new() { Text = "Remove", AutoSize = true };
    private readonly RadioButton _modeOverlay = new() { Text = "Overlay", AutoSize = true };
    private readonly RadioButton _modeHardware = new() { Text = "Monitor backlight", AutoSize = true };
    private readonly RadioButton _modeBoth = new() { Text = "Both (darkest)", AutoSize = true };
    private readonly TrackBar _backlight = Slider(100);
    private readonly Label _backlightValue = new() { AutoSize = true };
    private readonly TrackBar _overlay = Slider(Settings.MaxOverlayStrength);
    private readonly Label _overlayValue = new() { AutoSize = true };
    private readonly CheckBox _spotlight = new() { Text = "Also darken around the window on its own screen", AutoSize = true };
    private readonly Label _singleMonitorNote = Note(
        "One monitor connected: SideDim darkens around the focused window." + Environment.NewLine
        + "Backlight dimming needs a second monitor.");
    private readonly CheckBox _autostart = new() { Text = "Start with Windows", AutoSize = true };

    private readonly ComboBox _screen = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _identify = new() { Text = "Identify screens", AutoSize = true };
    private readonly CheckBox _screenDim = new() { Text = "Dim this screen", AutoSize = true };
    private readonly CheckBox _screenOwn = new() { Text = "Own darkness for this screen", AutoSize = true };
    private readonly TrackBar _screenBacklight = Slider(100);
    private readonly Label _screenBacklightValue = new() { AutoSize = true };
    private readonly TrackBar _screenOverlay = Slider(Settings.MaxOverlayStrength);
    private readonly Label _screenOverlayValue = new() { AutoSize = true };
    private readonly Label _screenNote = Note("Screens without their own darkness use the sliders on the Simple tab.");
    private readonly NumericUpDown _delay = Milliseconds(Settings.MaxDelayMs);
    private readonly NumericUpDown _restoreDelay = Milliseconds(Settings.MaxDelayMs);
    private readonly NumericUpDown _fade = Milliseconds(Settings.MaxFadeMs);
    private readonly TextBox _hotkey = new() { ReadOnly = true, BackColor = SystemColors.Window };
    private readonly Label _hotkeyStatus = new() { AutoSize = true, ForeColor = Color.Firebrick };

    private readonly CheckBox _test = new() { Text = "Test: dim around this window", Appearance = Appearance.Button, AutoSize = true };

    private bool _loading;
    private bool _dimNowStartedHere;

    public SettingsForm(Settings settings, ISettingsHost host)
    {
        _s = settings;
        _host = host;
        _sliderCommit = new System.Windows.Forms.Timer(_components) { Interval = (int)SliderSettle.TotalMilliseconds };

        Text = "SideDim";
        _windowIcon = AppIcon.Load(SystemInformation.IconSize);
        Icon = _windowIcon;
        Font = _uiFont ?? Font;
        _headerFont = new Font(Font.FontFamily, Font.Size * 1.1f, FontStyle.Bold);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        BuildLayout();
        ReloadValues();
        Wire();
    }

    private static TrackBar Slider(int max) =>
        new() { Minimum = 0, Maximum = max, TickFrequency = 10, SmallChange = 1, LargeChange = 10, AutoSize = false };

    private static NumericUpDown Milliseconds(int max) => new() { Minimum = 0, Maximum = max, Increment = 100 };

    private static Label Note(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText };

    /// <remarks>A TabControl does not size itself to its pages, and its chrome can only be measured once its handle exists.</remarks>
    private void BuildLayout()
    {
        var w = LogicalToDeviceUnits(460);
        _apps.Size = new Size(w, LogicalToDeviceUnits(130));
        _apps.Columns.Add("App", LogicalToDeviceUnits(130));
        _apps.Columns.Add("Location", w - LogicalToDeviceUnits(155));
        var iconSize = LogicalToDeviceUnits(16);
        _icons.ImageSize = new Size(iconSize, iconSize);
        _apps.SmallImageList = _icons;
        foreach (var slider in new[] { _backlight, _overlay, _screenBacklight, _screenOverlay })
            slider.Size = new Size(LogicalToDeviceUnits(240), LogicalToDeviceUnits(32));
        foreach (var box in new[] { _delay, _restoreDelay, _fade }) box.Width = LogicalToDeviceUnits(70);
        _hotkey.Width = LogicalToDeviceUnits(160);
        _screen.Width = LogicalToDeviceUnits(270);

        var simple = Page();
        simple.Controls.Add(_enabled);
        simple.Controls.Add(Header("When to dim the other screens"));
        simple.Controls.Add(_triggerApps);
        simple.Controls.Add(Indent(_apps));
        simple.Controls.Add(Indent(Row(_addExe, _addRunning, _remove)));
        simple.Controls.Add(Indent(_alsoFullscreen));
        simple.Controls.Add(_triggerAny);
        simple.Controls.Add(Header("How to dim"));
        simple.Controls.Add(Row(_modeOverlay, _modeHardware, _modeBoth));
        simple.Controls.Add(_singleMonitorNote);
        simple.Controls.Add(Row(Caption("Backlight while dimmed"), _backlight, _backlightValue));
        simple.Controls.Add(Row(Caption("Overlay darkness"), _overlay, _overlayValue));
        simple.Controls.Add(_spotlight);
        _autostart.Margin = new Padding(_autostart.Margin.Left, LogicalToDeviceUnits(12), 0, 0);
        simple.Controls.Add(_autostart);

        var advanced = Page();
        advanced.Controls.Add(Header("Screens", first: true));
        advanced.Controls.Add(Row(Caption("Screen"), _screen, _identify));
        advanced.Controls.Add(_screenDim);
        advanced.Controls.Add(_screenOwn);
        advanced.Controls.Add(Row(Caption("Backlight while dimmed"), _screenBacklight, _screenBacklightValue));
        advanced.Controls.Add(Row(Caption("Overlay darkness"), _screenOverlay, _screenOverlayValue));
        advanced.Controls.Add(_screenNote);
        advanced.Controls.Add(Header("Timing"));
        advanced.Controls.Add(Row(Caption("Wait before dimming (ms)"), _delay));
        advanced.Controls.Add(Row(Caption("Wait before restoring (ms)"), _restoreDelay));
        advanced.Controls.Add(Row(Caption("Fade in and out (ms)"), _fade));
        advanced.Controls.Add(Header("Hotkey"));
        advanced.Controls.Add(Row(Caption("Dim now hotkey"), _hotkey, _hotkeyStatus));

        var tabs = new TabControl();
        tabs.TabPages.Add(PageTab("Simple", simple));
        tabs.TabPages.Add(PageTab("Advanced", advanced));
        tabs.Size = new Size(1000, 1000);
        _ = tabs.Handle;
        var chrome = tabs.Size - tabs.DisplayRectangle.Size;
        var content = new Size(
            Math.Max(simple.PreferredSize.Width, advanced.PreferredSize.Width),
            Math.Max(simple.PreferredSize.Height, advanced.PreferredSize.Height));
        tabs.Size = content + chrome;
        tabs.Margin = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(10), LogicalToDeviceUnits(10), 0);

        var folder = new LinkLabel { Text = "Open settings and log folder", AutoSize = true, Anchor = AnchorStyles.Left };
        folder.LinkClicked += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.Folder);
            Process.Start(new ProcessStartInfo(AppPaths.Folder) { UseShellExecute = true })?.Dispose();
        };
        var version = new Label { Text = $"Version {Application.ProductVersion}", AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left };
        var footer = Row(_test, folder, version);
        footer.Margin = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(8), LogicalToDeviceUnits(10), LogicalToDeviceUnits(10));
        folder.Margin = version.Margin = new Padding(LogicalToDeviceUnits(14), 0, 0, 0);

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        root.Controls.Add(tabs);
        root.Controls.Add(footer);
        Controls.Add(root);

        var tip = new ToolTip(_components);
        tip.SetToolTip(_modeOverlay, "A black click-through layer. Works on every monitor.");
        tip.SetToolTip(_modeHardware, "Turns the monitor's real brightness down over DDC/CI. Less light in the room.");
        tip.SetToolTip(_modeBoth, "Backlight down and an overlay on top. As dark as it gets without turning screens off.");
        tip.SetToolTip(_backlight, "Monitor brightness (0-100) while dimmed. Screens already darker than this are left alone.");
        tip.SetToolTip(_spotlight, "Uses an overlay on the focused screen, with a hole for the window. Follows the window around.");
        tip.SetToolTip(_test, "Uses this settings window as the focused app, so you can tune everything live. Click again to stop.");
        tip.SetToolTip(_hotkey, "Click here and press a key combination with Ctrl or Alt, for example Ctrl+Alt+F9.");
        tip.SetToolTip(_autostart, "Starts SideDim in the tray when you log in.");
        tip.SetToolTip(_identify, "Shows each screen's number and name on the screen itself for a few seconds.");
        tip.SetToolTip(_screenDim, "Off: SideDim never darkens this screen, for example the one with your stream chat.");
        tip.SetToolTip(_restoreDelay, "How long focus must be away before the screens come back.");
    }

    private FlowLayoutPanel Page() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        Padding = new Padding(LogicalToDeviceUnits(10)),
    };

    private static TabPage PageTab(string title, Control content)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    private Label Header(string text, bool first = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = _headerFont,
        Margin = new Padding(0, LogicalToDeviceUnits(first ? 0 : 14), 0, LogicalToDeviceUnits(4)),
    };

    private Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = LogicalToDeviceUnits(170),
        Height = LogicalToDeviceUnits(28),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private Control Indent(Control c)
    {
        c.Margin = new Padding(LogicalToDeviceUnits(20), c.Margin.Top, c.Margin.Right, c.Margin.Bottom);
        return c;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        foreach (var c in controls)
        {
            c.Anchor = AnchorStyles.Left;
            row.Controls.Add(c);
        }
        return row;
    }

    public void ReloadValues()
    {
        _loading = true;
        _enabled.Checked = _s.Enabled;
        _triggerApps.Checked = _s.Trigger == DimTrigger.SelectedApps;
        _triggerAny.Checked = _s.Trigger == DimTrigger.AnyWindow;
        _alsoFullscreen.Checked = _s.AlsoAnyFullscreen;
        _modeOverlay.Checked = _s.Mode == DimMode.Overlay;
        _modeHardware.Checked = _s.Mode == DimMode.Hardware;
        _modeBoth.Checked = _s.Mode == DimMode.Both;
        _backlight.Value = Math.Clamp(_s.BacklightLevel, _backlight.Minimum, _backlight.Maximum);
        _overlay.Value = Math.Clamp(_s.OverlayStrength, _overlay.Minimum, _overlay.Maximum);
        _spotlight.Checked = _s.Spotlight || IsSingleMonitor;
        _test.Checked = _host.IsDimNowOn;
        _delay.Value = Math.Clamp(_s.DimDelayMs, (int)_delay.Minimum, (int)_delay.Maximum);
        _restoreDelay.Value = Math.Clamp(_s.RestoreDelayMs, (int)_restoreDelay.Minimum, (int)_restoreDelay.Maximum);
        _fade.Value = Math.Clamp(_s.FadeMs, (int)_fade.Minimum, (int)_fade.Maximum);
        _hotkey.Text = _s.ToggleHotkey;
        _autostart.Checked = Autostart.IsOn;
        ReloadApps();
        _loading = false;
        RefreshScreens();
        UpdateSliderLabels();
        UpdateHotkeyStatus();
        UpdateEnabledStates();
    }

    public void ReloadApps()
    {
        _apps.BeginUpdate();
        _apps.Items.Clear();
        _icons.Images.Clear();
        foreach (var image in _appImages) image.Dispose();
        _appImages.Clear();
        foreach (var entry in _s.Apps)
        {
            var hasPath = Path.IsPathRooted(entry);
            var item = new ListViewItem(Settings.AppName(entry)) { Tag = entry, ImageIndex = AddIcon(entry) };
            item.SubItems.Add(hasPath ? Path.GetDirectoryName(entry) : "(any location)");
            _apps.Items.Add(item);
        }
        _apps.EndUpdate();
        UpdateEnabledStates();
    }

    /// <remarks>ImageList never disposes the images added to it, so <see cref="_appImages"/> owns them.</remarks>
    private int AddIcon(string entry)
    {
        var image = ExeIcon(entry) ?? SystemIcons.Application.ToBitmap();
        _appImages.Add(image);
        _icons.Images.Add(image);
        return _icons.Images.Count - 1;
    }

    private static Bitmap? ExeIcon(string path)
    {
        if (!Path.IsPathRooted(path) || AppPicker.IsNetworkPath(path)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    /// <remarks>The hotkey box uses GotFocus and LostFocus, not Enter and Leave, so alt-tabbing away from it also turns the global hotkey back on.</remarks>
    private void Wire()
    {
        _enabled.CheckedChanged += (_, _) => Commit(() => _s.Enabled = _enabled.Checked);
        _triggerApps.CheckedChanged += (_, _) => Commit(() => _s.Trigger = _triggerApps.Checked ? DimTrigger.SelectedApps : DimTrigger.AnyWindow);
        _alsoFullscreen.CheckedChanged += (_, _) => Commit(() => _s.AlsoAnyFullscreen = _alsoFullscreen.Checked);
        _modeOverlay.CheckedChanged += (_, _) => { if (_modeOverlay.Checked) Commit(() => _s.Mode = DimMode.Overlay); };
        _modeHardware.CheckedChanged += (_, _) => { if (_modeHardware.Checked) Commit(() => _s.Mode = DimMode.Hardware); };
        _modeBoth.CheckedChanged += (_, _) => { if (_modeBoth.Checked) Commit(() => _s.Mode = DimMode.Both); };
        _spotlight.CheckedChanged += (_, _) => Commit(() => _s.Spotlight = _spotlight.Checked);
        _autostart.CheckedChanged += (_, _) =>
        {
            if (_loading || Autostart.TrySet(_autostart.Checked)) return;
            _loading = true;
            _autostart.Checked = Autostart.IsOn;
            _loading = false;
            MessageBox.Show(this, "Windows did not allow changing Start with Windows. See the log for details.", "SideDim",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        foreach (var slider in new[] { _backlight, _overlay }) slider.ValueChanged += (_, _) => OnSliderMoved();
        _screenBacklight.ValueChanged += (_, _) => OnScreenSliderMoved(m => m.BacklightLevel = _screenBacklight.Value);
        _screenOverlay.ValueChanged += (_, _) => OnScreenSliderMoved(m => m.OverlayStrength = _screenOverlay.Value);
        foreach (var box in new[] { _delay, _restoreDelay, _fade }) box.ValueChanged += (_, _) => OnSliderMoved();
        _sliderCommit.Tick += (_, _) => CommitSliders();

        _screen.SelectedIndexChanged += (_, _) => { if (!_loading) LoadSelectedScreen(); };
        _identify.Click += (_, _) => IdentifyWindows.Show(_screens);
        _screenDim.CheckedChanged += (_, _) =>
        {
            if (_loading || SelectedScreen is not { } screen) return;
            Commit(() => _s.MonitorFor(screen.Monitor.Id, screen.Name).Dim = _screenDim.Checked);
            RefreshScreenNames();
        };
        _screenOwn.CheckedChanged += (_, _) =>
        {
            if (_loading || SelectedScreen is not { } screen) return;
            Commit(() =>
            {
                var m = _s.MonitorFor(screen.Monitor.Id, screen.Name);
                m.BacklightLevel = _screenOwn.Checked ? _screenBacklight.Value : null;
                m.OverlayStrength = _screenOwn.Checked ? _screenOverlay.Value : null;
            });
            LoadSelectedScreen();
            RefreshScreenNames();
        };

        _test.CheckedChanged += (_, _) =>
        {
            if (_loading || _test.Checked == _host.IsDimNowOn) return;
            _host.ToggleTest();
            _dimNowStartedHere = _host.IsDimNowOn;
        };
        _host.DimNowChanged += SyncDimNowButton;
        _host.DisplaysChanged += OnDisplaysChanged;

        _apps.SelectedIndexChanged += (_, _) => UpdateEnabledStates();
        _apps.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        _remove.Click += (_, _) => RemoveSelected();
        _addExe.Click += (_, _) => BrowseForExe();
        _addRunning.Click += (_, _) => ShowRunningApps();

        _hotkey.GotFocus += (_, _) => _host.SuspendHotkey(true);
        _hotkey.LostFocus += (_, _) =>
        {
            _host.SuspendHotkey(false);
            UpdateHotkeyStatus();
        };
        _hotkey.KeyDown += OnHotkeyKeyDown;
    }

    private void Commit(Action change)
    {
        if (_loading) return;
        change();
        UpdateEnabledStates();
        _host.SaveAndApplySettings();
    }

    private void OnSliderMoved()
    {
        UpdateSliderLabels();
        if (_loading) return;
        _sliderCommit.Stop();
        _sliderCommit.Start();
    }

    /// <remarks>Writes at once to the screen selected now, so a mid-drag screen switch cannot hit the wrong screen and an untouched level stays inherited.</remarks>
    private void OnScreenSliderMoved(Action<MonitorSettings> write)
    {
        if (!_loading && _screenOwn.Checked && SelectedScreen is { } screen) write(_s.MonitorFor(screen.Monitor.Id, screen.Name));
        OnSliderMoved();
    }

    private void CommitSliders()
    {
        _sliderCommit.Stop();
        Commit(() =>
        {
            _s.BacklightLevel = _backlight.Value;
            _s.OverlayStrength = _overlay.Value;
            _s.DimDelayMs = (int)_delay.Value;
            _s.RestoreDelayMs = (int)_restoreDelay.Value;
            _s.FadeMs = (int)_fade.Value;
        });
        LoadSelectedScreen();
        RefreshScreenNames();
    }

    private void UpdateSliderLabels()
    {
        _backlightValue.Text = $"{_backlight.Value}";
        _overlayValue.Text = $"{_overlay.Value}%";
        _screenBacklightValue.Text = $"{_screenBacklight.Value}";
        _screenOverlayValue.Text = $"{_screenOverlay.Value}%";
    }

    private void UpdateHotkeyStatus() =>
        _hotkeyStatus.Text = _host.HotkeyWorks ? "" : "In use by another app, pick another";

    private bool IsSingleMonitor => !DimPolicy.BacklightAvailable(_host.MonitorCount);

    private void UpdateEnabledStates()
    {
        var apps = _triggerApps.Checked;
        _apps.Enabled = _addExe.Enabled = _addRunning.Enabled = _alsoFullscreen.Enabled = apps;
        _remove.Enabled = apps && _apps.SelectedItems.Count > 0;

        var single = IsSingleMonitor;
        _singleMonitorNote.Visible = single;
        _modeHardware.Enabled = _modeBoth.Enabled = _spotlight.Enabled = !single;
        var backlightUsed = !single && !_modeOverlay.Checked;
        var overlayUsed = single || !_modeHardware.Checked || _spotlight.Checked;
        _backlight.Enabled = backlightUsed;
        _overlay.Enabled = overlayUsed;

        var screenChosen = SelectedScreen is not null;
        _screenDim.Enabled = screenChosen;
        _screenOwn.Enabled = screenChosen && _screenDim.Checked;
        var own = _screenOwn.Enabled && _screenOwn.Checked;
        _screenBacklight.Enabled = own && backlightUsed;
        _screenOverlay.Enabled = own && overlayUsed;
    }

    private ScreenInfo? SelectedScreen => (_screen.SelectedItem as ScreenChoice)?.Screen;

    private void RefreshScreens()
    {
        var selectedId = SelectedScreen?.Monitor.Id;
        _screens = ScreenList.Describe(Monitors.All(), MonitorNames.Query());
        _loading = true;
        _screen.Items.Clear();
        foreach (var screen in _screens) _screen.Items.Add(new ScreenChoice(screen, _s));
        _screen.SelectedIndex = _screens.Count == 0 ? -1 : Math.Max(0, _screens.FindIndex(s => s.Monitor.Id == selectedId));
        _loading = false;
        LoadSelectedScreen();
    }

    private void RefreshScreenNames()
    {
        var selected = _screen.SelectedIndex;
        _loading = true;
        for (var i = 0; i < _screen.Items.Count; i++) _screen.Items[i] = new ScreenChoice(((ScreenChoice)_screen.Items[i]!).Screen, _s);
        _screen.SelectedIndex = selected;
        _loading = false;
    }

    private void LoadSelectedScreen()
    {
        if (SelectedScreen is not { } screen) return;
        var levels = _s.LevelsFor(screen.Monitor.Id);
        _loading = true;
        _screenDim.Checked = levels.Dim;
        _screenOwn.Checked = _s.Monitors.TryGetValue(screen.Monitor.Id, out var m) && m.HasOwnLevels;
        _screenBacklight.Value = Math.Clamp(levels.BacklightLevel, 0, 100);
        _screenOverlay.Value = Math.Clamp(levels.OverlayStrength, 0, Settings.MaxOverlayStrength);
        _loading = false;
        UpdateSliderLabels();
        UpdateEnabledStates();
    }

    private void OnDisplaysChanged()
    {
        _loading = true;
        _spotlight.Checked = _s.Spotlight || IsSingleMonitor;
        _loading = false;
        RefreshScreens();
        UpdateEnabledStates();
    }

    private void SyncDimNowButton()
    {
        if (!_host.IsDimNowOn) _dimNowStartedHere = false;
        _loading = true;
        _test.Checked = _host.IsDimNowOn;
        _loading = false;
    }

    private void AddApp(string entry)
    {
        if (!_s.AddApp(entry)) return;
        ReloadApps();
        _host.SaveAndApplySettings();
    }

    private void RemoveApp(string name)
    {
        if (!_s.RemoveApp(name)) return;
        ReloadApps();
        _host.SaveAndApplySettings();
    }

    private void RemoveSelected()
    {
        if (_apps.SelectedItems.Count == 0) return;
        foreach (ListViewItem item in _apps.SelectedItems) _s.RemoveApp((string)item.Tag!);
        ReloadApps();
        _host.SaveAndApplySettings();
    }

    private void BrowseForExe()
    {
        using var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick an app or game to dim for" };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddApp(dlg.FileName);
    }

    private void ShowRunningApps()
    {
        var menu = new ContextMenuStrip { ImageScalingSize = _icons.ImageSize };
        foreach (var app in AppPicker.Pickable(ForegroundWatcher.VisibleApps(), Environment.ProcessId, _s))
        {
            var path = app.Path;
            var listed = Settings.ListContains(_s.Apps, app.Name);
            var title = app.Title.Length > 50 ? app.Title[..50] + "…" : app.Title;
            var item = new ToolStripMenuItem($"{app.Name}   ({title})")
            {
                Checked = listed,
                Image = path is null ? null : ExeIcon(path),
            };
            var entry = path ?? app.Name;
            item.Click += (_, _) =>
            {
                if (listed) RemoveApp(app.Name);
                else AddApp(entry);
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem("No windowed apps running") { Enabled = false });
        menu.Closed += (_, _) => BeginInvoke(() =>
        {
            foreach (ToolStripItem item in menu.Items) item.Image?.Dispose();
            menu.Dispose();
        });
        menu.Show(_addRunning, new Point(0, _addRunning.Height));
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        if (Hotkey.FromKeyPress(e.KeyCode, e.Control, e.Alt, e.Shift) is not { } hotkey) return;

        _hotkey.Text = hotkey.ToString();
        _s.ToggleHotkey = hotkey.ToString();
        _s.Save();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.DimNowChanged -= SyncDimNowButton;
        _host.DisplaysChanged -= OnDisplaysChanged;
        if (_sliderCommit.Enabled) CommitSliders();
        if (_dimNowStartedHere && _host.IsDimNowOn) _host.ToggleTest();
        _host.SuspendHotkey(false);
        base.OnFormClosed(e);
    }

    /// <remarks>Images, fonts and the icon are disposed after <c>base.Dispose</c>, so no control paints with a disposed font or icon.</remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _components.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var image in _appImages) image.Dispose();
            _headerFont.Dispose();
            _uiFont?.Dispose();
            _windowIcon.Dispose();
        }
    }

    private sealed class ScreenChoice(ScreenInfo screen, Settings settings)
    {
        public ScreenInfo Screen { get; } = screen;

        public override string ToString()
        {
            if (!settings.Monitors.TryGetValue(Screen.Monitor.Id, out var m)) return Screen.Label;
            if (!m.Dim) return $"{Screen.Label}: never dimmed";
            return m.HasOwnLevels ? $"{Screen.Label}: own darkness" : Screen.Label;
        }
    }
}
