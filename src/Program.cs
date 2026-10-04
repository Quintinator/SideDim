namespace SideDim;

internal static class Program
{
    private const string SingleInstanceMutex = @"Local\SideDim-single-instance";
    private const string ShowSettingsEvent = @"Local\SideDim-show-settings";

    /// <remarks>SetDefaultDllDirectories comes first so Windows' own name-only DLL loads never search the exe's folder. A second launch needs AllowSetForegroundWindow or the running copy's settings window opens behind the active window. Crash handlers must be registered before TrayApp is constructed.</remarks>
    [STAThread]
    private static int Main(string[] args)
    {
        if (!Native.SetDefaultDllDirectories(Native.LOAD_LIBRARY_SEARCH_SYSTEM32))
            Log.Write($"SetDefaultDllDirectories failed (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");

        var startup = StartupArgs.Parse(args);
        if (startup.Probe) return Probe();

        using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var first);
        if (!first && startup.WaitForPrevious) first = WaitForPrevious(mutex);
        if (!first)
        {
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            showSettings.Set();
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();
        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        TrayApp? app = null;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            app?.EmergencyRestore();
            Log.Write($"Crash: {e.ExceptionObject}");
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

        if (startup.RemoveOldCopy is { } oldCopy) RemoveOldCopy(oldCopy);

        var started = app;
        if (startup.ShowSettings) ui.Post(_ => started.ShowSettings(), null);
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

    /// <remarks>The previous copy restores brightness before it exits. It never releases the mutex, so it arrives abandoned.</remarks>
    private static bool WaitForPrevious(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(60));
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void RemoveOldCopy(string oldExe)
    {
        if (Environment.ProcessPath is not { } self || !InstallLocation.MayRemoveOldCopy(oldExe, self)) return;
        try
        {
            File.Delete(oldExe);
            Log.Write($"Removed the old copy at {oldExe}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Write($"Could not remove the old copy at {oldExe}: {e.Message}");
        }
    }

    /// <summary>A UI error in the 100 ms poll repeats ten times a second; logging only a count keeps rotation from losing the first stack trace.</summary>
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
