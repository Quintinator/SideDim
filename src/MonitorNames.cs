using System.Runtime.InteropServices;

namespace SideDim;

internal static class MonitorNames
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO
    {
        public LUID AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO
    {
        public PATH_SOURCE_INFO Source;
        public PATH_TARGET_INFO Target;
        public uint Flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO without its never-read union; Size must stay 64 to match the native struct.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct MODE_INFO
    {
        public uint InfoType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_DEVICE_NAME
    {
        public uint Type;
        public uint Size;
        public LUID AdapterId;
        public uint Id;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PATH_INFO[] paths,
        ref uint modeCount, [Out] MODE_INFO[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME request);

    /// <summary>Keyed by the same device path as <see cref="Monitor.Id"/>; empty with old drivers or over remote desktop.</summary>
    public static Dictionary<string, string> Query()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (QueryPaths() is not { } paths) return names;

        foreach (var path in paths)
        {
            var request = new TARGET_DEVICE_NAME
            {
                Type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                Size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>(),
                AdapterId = path.Target.AdapterId,
                Id = path.Target.Id,
            };
            if (DisplayConfigGetDeviceInfo(ref request) == 0
                && !string.IsNullOrWhiteSpace(request.DevicePath) && !string.IsNullOrWhiteSpace(request.FriendlyName))
                names[request.DevicePath] = request.FriendlyName.Trim();
        }
        return names;
    }

    /// <remarks>The layout can change between the size query and the query itself; Microsoft documents retrying on ERROR_INSUFFICIENT_BUFFER.</remarks>
    private static PATH_INFO[]? QueryPaths()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return null;
            var paths = new PATH_INFO[pathCount];
            var modes = new MODE_INFO[modeCount];
            var result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result == ERROR_INSUFFICIENT_BUFFER) continue;
            return result == 0 ? paths.Take((int)pathCount).ToArray() : null;
        }
        return null;
    }
}
