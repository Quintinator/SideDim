using System.Runtime.InteropServices;

namespace SideDim;

/// <summary>A display as Windows sees it. Device is the key used everywhere (\\.\DISPLAY1 etc).</summary>
internal sealed record Monitor(IntPtr Handle, string Device, Rectangle Bounds);

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
            ? new Monitor(hMonitor, info.szDevice, info.rcMonitor.ToRectangle())
            : null;
    }
}

/// <summary>
/// Real monitor brightness over DDC/CI (VESA MCCS VCP code 0x10) through dxva2.
/// Values are exposed as 0-100 even for the rare monitor whose scale has another maximum.
/// </summary>
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

    /// <summary>Which monitors answer DDC/CI and their brightness, for --probe.</summary>
    public Dictionary<string, uint?> Probe() => Monitors.All().ToDictionary(m => m.Device, m => Read(m.Device));

    private static Monitor? Find(string device) => Monitors.All().FirstOrDefault(m => m.Device == device);

    /// <summary>Runs fn against each physical monitor behind an HMONITOR; returns the first non-null answer.</summary>
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
                // DDC/CI is a slow I2C bus; one retry fixes most transient failures.
                var r = fn(p.hPhysicalMonitor);
                if (r is null)
                {
                    Thread.Sleep(RetryPause);
                    r = fn(p.hPhysicalMonitor);
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
