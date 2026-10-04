using System.Diagnostics;
using System.Text;

namespace SideDim;

/// <summary>ProcessId is the hosted app for Store apps, Process is "" when unknown, Path is null when unknown, Bounds is in physical pixels.</summary>
internal sealed record Foreground(uint ProcessId, string Process, string? Path, Monitor Monitor, Rectangle Bounds, bool IsFullscreen);

internal static class ForegroundWatcher
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
    };

    private static readonly int OwnPid = Environment.ProcessId;

    private static IntPtr _cachedWindow;
    private static (uint Pid, string Name, string? Path) _cachedProcess = (0, "", null);

    /// <remarks>No-activate overlays are always excluded, because Windows can briefly report them as the foreground window.</remarks>
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

    /// <remarks>Cached by window handle, not pid, because pids get reused; failed lookups stay uncached so the next poll retries them.</remarks>
    private static (uint Pid, string Name, string? Path) ProcessInfo(IntPtr hwnd, uint pid)
    {
        if (hwnd == _cachedWindow) return _cachedProcess;

        var owner = Resolve(hwnd, pid);

        if (owner.Name.Length == 0) return owner;
        _cachedWindow = hwnd;
        _cachedProcess = owner;
        return owner;
    }

    /// <remarks>ApplicationFrameHost wraps Store and Game Pass apps; the real app owns a child window that can be missing while it resumes, which gives an empty name.</remarks>
    private static (uint Pid, string Name, string? Path) Resolve(IntPtr hwnd, uint pid)
    {
        var (name, path) = Describe(pid);
        if (string.Equals(name, StoreAppFrame, StringComparison.OrdinalIgnoreCase))
        {
            pid = HostedAppPid(hwnd, pid);
            (name, path) = pid == 0 ? ("", null) : Describe(pid);
        }
        return (pid, name, path);
    }

    /// <remarks>Cloaked windows are skipped because suspended Store apps keep an invisible frame around.</remarks>
    public static List<WindowedApp> VisibleApps()
    {
        var apps = new List<WindowedApp>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
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
            }
        }
        return (name, path);
    }
}
