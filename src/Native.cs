using System.Runtime.InteropServices;
using System.Text;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SideDim;

/// <remarks>Every import loads from System32 only (assembly attribute above): dxva2.dll and dwmapi.dll aren't KnownDLLs, so a planted copy next to the exe would otherwise be loaded.</remarks>
internal static class Native
{
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_TOPMOST = 0x8;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_NOACTIVATE = 0x8000000;

    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_DPICHANGED = 0x02E0;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint SWP_NOACTIVATE = 0x10;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const byte VCP_BRIGHTNESS = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    public delegate bool WindowEnumProc(IntPtr hWnd, IntPtr data);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, WindowEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    public const uint GW_OWNER = 4;
    public const int DWMWA_CLOAKED = 14;

    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint command);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeInt(IntPtr hWnd, int attribute, out int value, int size);

    public const int ASFW_ANY = -1;

    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MAPVK_VK_TO_VSC = 0;
    public const uint TOUNICODE_KEEP_STATE = 0x4;

    [DllImport("user32.dll")] public static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(uint vk, uint scanCode, byte[] keyState, [Out] char[] buffer, int bufferSize, uint flags, IntPtr keyboardLayout);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CAPTION = 0x00C00000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);

    /// <remarks>Binds GetWindowLongW on purpose: 32-bit user32 does not export GetWindowLongPtrW, and styles fit in 32 bits.</remarks>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hWnd, int index);

    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    /// <remarks>Limited query access on purpose, so it also works for most elevated and anti-cheat protected games.</remarks>
    public static string? ProcessPath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <remarks>Prefers DWM frame bounds because GetWindowRect includes the invisible resize borders.</remarks>
    public static Rectangle VisibleBounds(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0)
            return r.ToRectangle();
        return GetWindowRect(hWnd, out r) ? r.ToRectangle() : Rectangle.Empty;
    }

    public const int ATTACH_PARENT_PROCESS = -1;
    public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;

    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetDefaultDllDirectories(uint directoryFlags);

    public static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [DllImport("kernel32.dll")] public static extern bool AttachConsole(int processId);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);
}
