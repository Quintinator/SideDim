using System.Runtime.InteropServices;

namespace SideDim;

/// <summary>Id is stable per monitor and port and keys saved brightness; Device (\\.\DISPLAY1) can be renumbered after a replug, so never persist it.</summary>
internal sealed record Monitor(IntPtr Handle, string Device, string Id, Rectangle Bounds);

internal static class Monitors
{
    public static List<Monitor> All()
    {
        var list = new List<Monitor>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            if (TryGet(h) is { } m) list.Add(m);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Monitor? FromWindow(IntPtr hwnd) =>
        TryGet(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST));

    private static Monitor? TryGet(IntPtr hMonitor)
    {
        var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
        return Native.GetMonitorInfo(hMonitor, ref info)
            ? new Monitor(hMonitor, info.szDevice, StableId(info.szDevice), info.rcMonitor.ToRectangle())
            : null;
    }

    private static string StableId(string device)
    {
        var dd = new Native.DISPLAY_DEVICE { cb = Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
        return Native.EnumDisplayDevices(device, 0, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME) && dd.DeviceID.Length > 0
            ? dd.DeviceID
            : device;
    }
}

internal sealed class DdcBrightnessDevice : IBrightnessDevice
{
    private static readonly TimeSpan RetryPause = TimeSpan.FromMilliseconds(40);
    private readonly Dictionary<string, uint> _maxima = [];

    public bool IsConnected(string device) => Find(device) is not null;

    public uint? Read(string device)
    {
        if (Find(device) is not { } m || ReadRaw(m) is not { } raw) return null;
        _maxima[device] = raw.Max;
        return raw.Max == 100 ? raw.Current : (uint)Math.Round(raw.Current * 100.0 / raw.Max);
    }

    public bool Write(string device, uint value)
    {
        if (Find(device) is not { } m) return false;
        if (!_maxima.TryGetValue(device, out var max))
        {
            if (ReadRaw(m) is not { } reading) return false;
            _maxima[device] = max = reading.Max;
        }
        var raw = max == 100 ? value : (uint)Math.Round(Math.Min(value, 100) * max / 100.0);
        return WithPhysical<bool?>(m.Handle, h => Native.SetVCPFeature(h, Native.VCP_BRIGHTNESS, raw) ? true : null) == true;
    }

    private static (uint Current, uint Max)? ReadRaw(Monitor m) => WithPhysical(m.Handle, h =>
        Native.GetVCPFeatureAndVCPFeatureReply(h, Native.VCP_BRIGHTNESS, IntPtr.Zero, out var current, out var max) && max > 0
            ? ((uint, uint)?)(Math.Min(current, max), max)
            : null);

    public Dictionary<string, uint?> Probe() => Monitors.All().ToDictionary(m => m.Device, m => Read(m.Id));

    /// <summary>Also matches the GDI name, for state files written by version 0.1.0.</summary>
    private static Monitor? Find(string key) => Monitors.All().FirstOrDefault(m => m.Id == key || m.Device == key);

    private static T? WithPhysical<T>(IntPtr hMonitor, Func<IntPtr, T?> fn)
    {
        if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0) return default;
        var physical = new Native.PHYSICAL_MONITOR[count];
        if (!Native.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return default;
        try
        {
            T? answer = default;
            foreach (var p in physical)
            {
                var r = fn(p.hPhysicalMonitor);
                if (r is null)
                {
                    Thread.Sleep(RetryPause);
                    r = fn(p.hPhysicalMonitor);
                    if (r is null) Log.Write($"DDC/CI call to '{p.szPhysicalMonitorDescription}' failed twice (error {Marshal.GetLastPInvokeError()}).");
                }
                answer ??= r;
            }
            return answer;
        }
        finally
        {
            Native.DestroyPhysicalMonitors(count, physical);
        }
    }
}
