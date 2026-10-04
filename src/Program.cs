namespace SideDim;

internal static class Program
{
    private const string SingleInstanceMutex = @"Local\SideDim-single-instance";
    private const string ShowSettingsEvent = @"Local\SideDim-show-settings";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase)) return Probe();

        // Starting the exe again while it runs opens the settings window of the running copy.
        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var first);
        if (!first)
        {
            // We were just started by the user, so we may hand our right to take the foreground to the
            // running copy; without this its settings window would open behind the active window.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            showSettings.Set();
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        // Handlers go up before the app exists, so even a failing constructor gets logged.
        TrayApp? app = null;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write($"Crash: {e.ExceptionObject}");
            app?.EmergencyRestore();
        };
        Application.ThreadException += new RepeatFilter().OnUiError;

        try
        {
            app = new TrayApp(ui);
        }
        catch (Exception e)
        {
            Log.Write($"Startup failed: {e}");
            MessageBox.Show($"SideDim could not start: {e.Message}{Environment.NewLine}Details are in {AppPaths.LogFile}",
                "SideDim", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        var started = app;
        var wait = ThreadPool.RegisterWaitForSingleObject(
            showSettings, (_, _) => ui.Post(_ => started.ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            Application.Run(app);
        }
        finally
        {
            wait.Unregister(null);
        }
        return 0;
    }

    /// <summary>
    /// A UI error inside the 100 ms poll repeats ten times a second; log the first one in full and then
    /// only a count, so the log doesn't rotate the original stack trace away.
    /// </summary>
    private sealed class RepeatFilter
    {
        private string? _last;
        private int _repeats;

        public void OnUiError(object sender, ThreadExceptionEventArgs e)
        {
            var text = e.Exception.ToString();
            if (text == _last)
            {
                if (++_repeats % 600 == 0) Log.Write($"Same UI error repeated {_repeats} times");
                return;
            }
            if (_repeats > 0) Log.Write($"Previous UI error repeated {_repeats} times");
            _last = text;
            _repeats = 0;
            Log.Write($"UI error: {text}");
        }
    }

    /// <summary>
    /// Lists which monitors answer DDC/CI and their brightness. Printed to the console when started
    /// from one, and always written to the log, so it can be pasted into a bug report.
    /// </summary>
    private static int Probe()
    {
        Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        Console.WriteLine();
        if (Mutex.TryOpenExisting(SingleInstanceMutex, out var running))
        {
            running.Dispose();
            const string note = "Note: SideDim is running; if it is dimming right now, these values are the dimmed ones.";
            Console.WriteLine(note);
            Log.Write(note);
        }
        var names = MonitorNames.Query();
        var monitors = Monitors.All().ToDictionary(m => m.Device);
        foreach (var (device, brightness) in new DdcBrightnessDevice().Probe())
        {
            var name = monitors.TryGetValue(device, out var m) && names.TryGetValue(m.Id, out var n) ? $" ({n})" : "";
            var line = $"probe {device}{name}: {(brightness is null ? "no DDC/CI" : $"brightness {brightness}")}";
            Console.WriteLine(line);
            Log.Write(line);
        }
        return 0;
    }
}
