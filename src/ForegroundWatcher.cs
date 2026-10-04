using System.Diagnostics;
using System.Text;

namespace SideDim;

/// <param name="ProcessId">The process that owns the window (the hosted app for Store apps).</param>
/// <param name="Process">Process name without ".exe", or "" when unknown.</param>
/// <param name="Path">Full exe path, or null if Windows won't tell us.</param>
/// <param name="Bounds">Visible window rectangle in physical pixels.</param>
internal sealed record Foreground(uint ProcessId, string Process, string? Path, Monitor Monitor, Rectangle Bounds, bool IsFullscreen);

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
    private static (uint Pid, string Name, string? Path) _cachedProcess = (0, "", null);

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
            if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_NOACTIVATE) != 0) return null;
        }

        var monitor = Monitors.FromWindow(hwnd);
        if (monitor is null) return null;

        var bounds = Native.VisibleBounds(hwnd);
        var hasCaption = (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION;
        var fullscreen = DimPolicy.IsFullscreen(bounds, monitor.Bounds, Native.IsZoomed(hwnd), hasCaption);

        var (ownerPid, name, path) = ProcessInfo(hwnd, pid);
        return new Foreground(ownerPid, name, path, monitor, bounds, fullscreen);
    }

    private static (uint Pid, string Name, string? Path) ProcessInfo(IntPtr hwnd, uint pid)
    {
        // Keyed on the window, not the pid: pids get reused, window handles of live windows don't.
        if (hwnd == _cachedWindow) return _cachedProcess;

        var owner = Resolve(hwnd, pid);

        // Only remember successes, so a lookup that failed (process starting up, access denied) is retried.
        if (owner.Name.Length == 0) return owner;
        _cachedWindow = hwnd;
        _cachedProcess = owner;
        return owner;
    }

    /// <summary>The process a user would say owns this window ("" name when Windows won't say).</summary>
    private static (uint Pid, string Name, string? Path) Resolve(IntPtr hwnd, uint pid)
    {
        var (name, path) = Describe(pid);
        if (string.Equals(name, StoreAppFrame, StringComparison.OrdinalIgnoreCase))
        {
            // Store and Game Pass apps live inside an ApplicationFrameHost window; the real app owns a child.
            // The child can be missing for a moment while the app resumes, so a miss is retried next poll.
            pid = HostedAppPid(hwnd, pid);
            (name, path) = pid == 0 ? ("", null) : Describe(pid);
        }
        return (pid, name, path);
    }

    /// <summary>
    /// Visible, titled top-level windows with their owning app, resolved the same way as the foreground
    /// window, so Store apps show up under their real name instead of ApplicationFrameHost.
    /// </summary>
    public static List<WindowedApp> VisibleApps()
    {
        var apps = new List<WindowedApp>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
            // Suspended Store apps keep an invisible ("cloaked") frame around.
            if (Native.DwmGetWindowAttributeInt(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            var length = Native.GetWindowTextLength(hwnd);
            if (length == 0 || Native.GetWindowThreadProcessId(hwnd, out var pid) == 0) return true;
            var title = new StringBuilder(length + 1);
            Native.GetWindowText(hwnd, title, title.Capacity);

            var (owner, name, path) = Resolve(hwnd, pid);
            if (name.Length > 0) apps.Add(new WindowedApp((int)owner, name, title.ToString(), path));
            return true;
        }, IntPtr.Zero);
        return apps;
    }

    private const string StoreAppFrame = "ApplicationFrameHost";

    private static uint HostedAppPid(IntPtr frame, uint framePid)
    {
        uint found = 0;
        Native.EnumChildWindows(frame, (child, _) =>
        {
            if (Native.GetWindowThreadProcessId(child, out var childPid) != 0 && childPid != framePid)
            {
                found = childPid;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static (string Name, string? Path) Describe(uint pid)
    {
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
        return (name, path);
    }
}
