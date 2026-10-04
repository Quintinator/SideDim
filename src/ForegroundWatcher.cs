using System.Diagnostics;
using System.Text;

namespace SideDim;

/// <param name="Bounds">Visible window rectangle in physical pixels.</param>
/// <param name="Path">Full exe path, or null if Windows won't tell us.</param>
internal sealed record Foreground(IntPtr Window, string Process, string? Path, Monitor Monitor, Rectangle Bounds, bool IsFullscreen);

internal static class ForegroundWatcher
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
    };

    private static readonly int OwnPid = Environment.ProcessId;

    // The foreground window rarely changes, so remember its process instead of opening it every poll.
    private static IntPtr _cachedWindow;
    private static (string Name, string? Path) _cachedProcess = ("", null);

    /// <summary>Returns the window the user is looking at, or null for desktop/shell (and our own windows, unless asked).</summary>
    public static Foreground? Current(bool includeOwnWindows = false)
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == Native.GetShellWindow() || hwnd == Native.GetDesktopWindow()) return null;
        if (Native.IsIconic(hwnd) || !Native.IsWindowVisible(hwnd)) return null;

        var cls = new StringBuilder(256);
        if (Native.GetClassName(hwnd, cls, cls.Capacity) > 0 && ShellClasses.Contains(cls.ToString())) return null;

        if (Native.GetWindowThreadProcessId(hwnd, out var pid) == 0) return null;
        if (pid == OwnPid)
        {
            if (!includeOwnWindows) return null;
            // Our overlays are never "the window you're using", even if Windows briefly says so.
            if (((long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_NOACTIVATE) != 0) return null;
        }

        var monitor = Monitors.FromWindow(hwnd);
        if (monitor is null) return null;

        var bounds = Native.VisibleBounds(hwnd);
        var hasCaption = ((long)Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION;
        var fullscreen = DimPolicy.IsFullscreen(bounds, monitor.Bounds, Native.IsZoomed(hwnd), hasCaption);

        var (name, path) = ProcessInfo(hwnd, pid);
        return new Foreground(hwnd, name, path, monitor, bounds, fullscreen);
    }

    private static (string, string?) ProcessInfo(IntPtr hwnd, uint pid)
    {
        // Keyed on the window, not the pid: pids get reused, window handles of live windows don't.
        if (hwnd == _cachedWindow) return _cachedProcess;

        var path = Native.ProcessPath(pid);
        var name = path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : "";
        if (name.Length == 0)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // The process exited between the two calls.
            }
        }

        _cachedWindow = hwnd;
        _cachedProcess = (name, path);
        return _cachedProcess;
    }
}
