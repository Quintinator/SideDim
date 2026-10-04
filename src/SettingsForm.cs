using System.ComponentModel;
using System.Diagnostics;

namespace SideDim;

/// <summary>Every control writes straight into Settings and applies immediately; there is no OK/Cancel.</summary>
internal sealed class SettingsForm : Form
{
    private static readonly TimeSpan SliderSettle = TimeSpan.FromMilliseconds(250);

    private readonly Settings _s;
    private readonly ISettingsHost _host;
    private readonly IContainer _components = new Container();
    private readonly System.Windows.Forms.Timer _sliderCommit;
    private readonly Font _headerFont;
    private readonly Icon _windowIcon;
    private readonly List<Image> _appImages = [];

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
    private readonly TrackBar _backlight = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10, AutoSize = false };
    private readonly Label _backlightValue = new() { AutoSize = true };
    private readonly TrackBar _overlay = new() { Minimum = 0, Maximum = Settings.MaxOverlayStrength, TickFrequency = 10, SmallChange = 1, LargeChange = 10, AutoSize = false };
    private readonly Label _overlayValue = new() { AutoSize = true };
    private readonly CheckBox _spotlight = new() { Text = "Also darken around the window on its own screen", AutoSize = true };
    private readonly CheckBox _test = new() { Text = "Test: dim around this window", Appearance = Appearance.Button, AutoSize = true };

    private readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = Settings.MaxDelayMs, Increment = 100, Width = 70 };
    private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 120, BackColor = SystemColors.Window };
    private readonly Label _hotkeyStatus = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly CheckBox _autostart = new() { Text = "Start with Windows", AutoSize = true };

    private bool _loading;

    public SettingsForm(Settings settings, ISettingsHost host)
    {
        _s = settings;
        _host = host;
        _sliderCommit = new System.Windows.Forms.Timer(_components) { Interval = (int)SliderSettle.TotalMilliseconds };

        Text = "SideDim";
        _windowIcon = AppIcon.Load(SystemInformation.IconSize);
        Icon = _windowIcon;
        Font = SystemFonts.MessageBoxFont ?? Font;
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

    private void BuildLayout()
    {
        var w = LogicalToDeviceUnits(460);
        _apps.Size = new Size(w, LogicalToDeviceUnits(130));
        _apps.Columns.Add("App", LogicalToDeviceUnits(130));
        _apps.Columns.Add("Location", w - LogicalToDeviceUnits(155));
        var iconSize = LogicalToDeviceUnits(16);
        _icons.ImageSize = new Size(iconSize, iconSize);
        _apps.SmallImageList = _icons;
        _backlight.Size = _overlay.Size = new Size(LogicalToDeviceUnits(260), LogicalToDeviceUnits(32));

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(LogicalToDeviceUnits(14)),
        };

        root.Controls.Add(_enabled);

        root.Controls.Add(Header("When to dim the other screens"));
        root.Controls.Add(_triggerApps);
        root.Controls.Add(Indent(_apps));
        root.Controls.Add(Indent(Row(_addExe, _addRunning, _remove)));
        root.Controls.Add(Indent(_alsoFullscreen));
        root.Controls.Add(_triggerAny);

        root.Controls.Add(Header("How to dim"));
        root.Controls.Add(Row(_modeOverlay, _modeHardware, _modeBoth));
        root.Controls.Add(Row(Caption("Backlight while dimmed"), _backlight, _backlightValue));
        root.Controls.Add(Row(Caption("Overlay darkness"), _overlay, _overlayValue));
        root.Controls.Add(_spotlight);
        root.Controls.Add(_test);

        root.Controls.Add(Header("Other"));
        root.Controls.Add(Row(Caption("Wait before dimming (ms)"), _delay));
        root.Controls.Add(Row(Caption("Dim now hotkey"), _hotkey, _hotkeyStatus));
        root.Controls.Add(_autostart);

        var folder = new LinkLabel { Text = "Open settings and log folder", AutoSize = true, Margin = new Padding(0, LogicalToDeviceUnits(14), 0, 0) };
        folder.LinkClicked += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.Folder);
            Process.Start(new ProcessStartInfo(AppPaths.Folder) { UseShellExecute = true })?.Dispose();
        };
        root.Controls.Add(folder);
        root.Controls.Add(new Label { Text = $"Version {Application.ProductVersion}", AutoSize = true, ForeColor = SystemColors.GrayText });

        var tip = new ToolTip(_components);
        tip.SetToolTip(_modeOverlay, "A black click-through layer. Works on every monitor.");
        tip.SetToolTip(_modeHardware, "Turns the monitor's real brightness down over DDC/CI. Less light in the room.");
        tip.SetToolTip(_modeBoth, "Backlight down and an overlay on top. As dark as it gets without turning screens off.");
        tip.SetToolTip(_backlight, "Monitor brightness (0-100) while dimmed. Screens already darker than this are left alone.");
        tip.SetToolTip(_spotlight, "Uses an overlay on the focused screen, with a hole for the window. Follows the window around.");
        tip.SetToolTip(_test, "Uses this settings window as the game, so you can tune everything live. Click again to stop.");
        tip.SetToolTip(_hotkey, "Click here and press a key combination with Ctrl or Alt, or a function key.");

        Controls.Add(root);
    }

    private Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = _headerFont,
        Margin = new Padding(0, LogicalToDeviceUnits(14), 0, LogicalToDeviceUnits(4)),
    };

    private Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = LogicalToDeviceUnits(160),
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

    /// <summary>Copies settings into the controls without triggering change handlers.</summary>
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
        _spotlight.Checked = _s.Spotlight;
        _test.Checked = _host.IsTesting;
        _delay.Value = Math.Clamp(_s.DimDelayMs, (int)_delay.Minimum, (int)_delay.Maximum);
        _hotkey.Text = _s.ToggleHotkey;
        _autostart.Checked = Autostart.IsOn;
        ReloadApps();
        _loading = false;
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

    private int AddIcon(string entry)
    {
        var image = ExeIcon(entry) ?? SystemIcons.Application.ToBitmap();
        _appImages.Add(image); // ImageList keeps a reference, so dispose these ourselves on reload/close
        _icons.Images.Add(image);
        return _icons.Images.Count - 1;
    }

    private static Bitmap? ExeIcon(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void Wire()
    {
        _enabled.CheckedChanged += (_, _) => Commit(() => _s.Enabled = _enabled.Checked);
        _triggerApps.CheckedChanged += (_, _) => Commit(() => _s.Trigger = _triggerApps.Checked ? DimTrigger.SelectedApps : DimTrigger.AnyWindow);
        _alsoFullscreen.CheckedChanged += (_, _) => Commit(() => _s.AlsoAnyFullscreen = _alsoFullscreen.Checked);
        _modeOverlay.CheckedChanged += (_, _) => { if (_modeOverlay.Checked) Commit(() => _s.Mode = DimMode.Overlay); };
        _modeHardware.CheckedChanged += (_, _) => { if (_modeHardware.Checked) Commit(() => _s.Mode = DimMode.Hardware); };
        _modeBoth.CheckedChanged += (_, _) => { if (_modeBoth.Checked) Commit(() => _s.Mode = DimMode.Both); };
        _spotlight.CheckedChanged += (_, _) => Commit(() => _s.Spotlight = _spotlight.Checked);
        _delay.ValueChanged += (_, _) => Commit(() => _s.DimDelayMs = (int)_delay.Value);
        _autostart.CheckedChanged += (_, _) => { if (!_loading) Autostart.IsOn = _autostart.Checked; };

        // Sliders update the label live but only commit once you stop moving them:
        // every DDC/CI write is a slow bus transaction, no point sending fifty of them.
        _backlight.ValueChanged += (_, _) => OnSliderMoved();
        _overlay.ValueChanged += (_, _) => OnSliderMoved();
        _sliderCommit.Tick += (_, _) => CommitSliders();

        _test.CheckedChanged += (_, _) => { if (!_loading && _test.Checked != _host.IsTesting) _host.ToggleTest(); };
        _host.TestingChanged += SyncTestButton;

        _apps.SelectedIndexChanged += (_, _) => UpdateEnabledStates();
        _apps.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        _remove.Click += (_, _) => RemoveSelected();
        _addExe.Click += (_, _) => BrowseForExe();
        _addRunning.Click += (_, _) => ShowRunningApps();

        _hotkey.Enter += (_, _) => _host.SuspendHotkey(true);
        _hotkey.Leave += (_, _) =>
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
        _host.SettingsChanged();
    }

    private void OnSliderMoved()
    {
        UpdateSliderLabels();
        if (_loading) return;
        _sliderCommit.Stop();
        _sliderCommit.Start();
    }

    private void CommitSliders()
    {
        _sliderCommit.Stop();
        Commit(() =>
        {
            _s.BacklightLevel = _backlight.Value;
            _s.OverlayStrength = _overlay.Value;
        });
    }

    private void UpdateSliderLabels()
    {
        _backlightValue.Text = $"{_backlight.Value}";
        _overlayValue.Text = $"{_overlay.Value}%";
    }

    private void UpdateHotkeyStatus() =>
        _hotkeyStatus.Text = _host.HotkeyWorks ? "" : "In use by another app, pick another";

    private void UpdateEnabledStates()
    {
        var apps = _triggerApps.Checked;
        _apps.Enabled = _addExe.Enabled = _addRunning.Enabled = _alsoFullscreen.Enabled = apps;
        _remove.Enabled = apps && _apps.SelectedItems.Count > 0;

        _backlight.Enabled = !_modeOverlay.Checked;
        _overlay.Enabled = !_modeHardware.Checked || _spotlight.Checked;
    }

    private void SyncTestButton()
    {
        _loading = true;
        _test.Checked = _host.IsTesting;
        _loading = false;
    }

    private void AddApp(string entry)
    {
        if (!_s.AddApp(entry)) return;
        ReloadApps();
        _host.SettingsChanged();
    }

    private void RemoveSelected()
    {
        if (_apps.SelectedItems.Count == 0) return;
        foreach (ListViewItem item in _apps.SelectedItems) _s.RemoveApp((string)item.Tag!);
        ReloadApps();
        _host.SettingsChanged();
    }

    private void BrowseForExe()
    {
        using var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick the game or app to dim for" };
        if (SteamLibraries.BiggestGamesFolder() is { } steam) dlg.InitialDirectory = steam;
        if (dlg.ShowDialog(this) == DialogResult.OK) AddApp(dlg.FileName);
    }

    private void ShowRunningApps()
    {
        var menu = new ContextMenuStrip { ImageScalingSize = _icons.ImageSize };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    if (!seen.Add(p.ProcessName) || Settings.ListContains(_s.NeverDimFor, p.ProcessName)) continue;

                    var path = Native.ProcessPath((uint)p.Id);
                    var title = p.MainWindowTitle.Length > 50 ? p.MainWindowTitle[..50] + "…" : p.MainWindowTitle;
                    var item = new ToolStripMenuItem($"{p.ProcessName}   ({title})")
                    {
                        Checked = Settings.ListContains(_s.Apps, p.ProcessName),
                        Image = path is null ? null : ExeIcon(path),
                    };
                    var entry = path ?? p.ProcessName;
                    item.Click += (_, _) => AddApp(entry);
                    menu.Items.Add(item);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited while we looked at it, or we may not query it.
                }
            }
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
        _s.Save(); // registered when the box loses focus, so the combo isn't grabbed mid-typing
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.TestingChanged -= SyncTestButton;
        if (_sliderCommit.Enabled) CommitSliders();
        if (_host.IsTesting) _host.ToggleTest();
        if (_hotkey.Focused) _host.SuspendHotkey(false);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _components.Dispose();
            _icons.Dispose();
            foreach (var image in _appImages) image.Dispose();
            _headerFont.Dispose();
            _windowIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
