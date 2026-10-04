namespace SideDim;

/// <summary>Shows a big number and name on every screen for a few seconds, like Windows' own "Identify".</summary>
internal static class IdentifyWindows
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(3);

    public static void Show(IEnumerable<ScreenInfo> screens)
    {
        foreach (var screen in screens) new Badge(screen).Show();
    }

    private sealed class Badge : Form
    {
        private readonly System.Windows.Forms.Timer _close = new() { Interval = (int)ShowFor.TotalMilliseconds };
        private readonly Font _numberFont;
        private readonly Font _nameFont;
        private readonly Rectangle _bounds;

        public Badge(ScreenInfo screen)
        {
            var m = screen.Monitor.Bounds;
            var size = new Size(Math.Max(m.Width / 5, 240), Math.Max(m.Height / 4, 200));
            _bounds = new Rectangle(m.X + (m.Width - size.Width) / 2, m.Y + (m.Height - size.Height) / 2, size.Width, size.Height);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(32, 32, 36);
            ForeColor = Color.White;
            Bounds = _bounds;
            Opacity = 0.92;

            string family;
            using (var ui = SystemFonts.MessageBoxFont) family = ui?.Name ?? "Segoe UI";
            _numberFont = new Font(family, size.Height * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel);
            _nameFont = new Font(family, size.Height * 0.09f, GraphicsUnit.Pixel);
            Controls.Add(new Label
            {
                Text = screen.Name + (screen.IsMain ? " (main)" : ""),
                Font = _nameFont,
                Dock = DockStyle.Bottom,
                Height = (int)(size.Height * 0.25),
                TextAlign = ContentAlignment.MiddleCenter,
            });
            Controls.Add(new Label
            {
                Text = screen.Number.ToString(),
                Font = _numberFont,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            });

            _close.Tick += (_, _) => Close();
            _close.Start();
            // The labels cover the whole badge, so they need the click handler too.
            Click += (_, _) => Close();
            foreach (Control label in Controls) label.Click += (_, _) => Close();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Physical pixels on the right monitor, without activating (same as the overlays).
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, Native.SWP_NOACTIVATE);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED) return; // keep the physical size we asked for
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            _close.Dispose();
            _numberFont.Dispose();
            _nameFont.Dispose();
        }
    }
}
