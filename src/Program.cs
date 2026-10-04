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
            showSettings.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        var app = new TrayApp(ui);
        var wait = ThreadPool.RegisterWaitForSingleObject(
            showSettings, (_, _) => ui.Post(_ => app.ShowSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write($"Crash: {e.ExceptionObject}");
            app.EmergencyRestore();
        };
        Application.ThreadException += (_, e) => Log.Write($"UI error: {e.Exception}");

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
    /// Lists which monitors answer DDC/CI and their brightness. Printed to the console when started
    /// from one, and always written to the log, so it can be pasted into a bug report.
    /// </summary>
    private static int Probe()
    {
        Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        Console.WriteLine();
        foreach (var (device, brightness) in new DdcBrightnessDevice().Probe())
        {
            var line = $"probe {device}: {(brightness is null ? "no DDC/CI" : $"brightness {brightness}")}";
            Console.WriteLine(line);
            Log.Write(line);
        }
        return 0;
    }
}
