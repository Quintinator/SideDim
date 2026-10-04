namespace SideDim;

internal readonly record struct WindowedApp(int ProcessId, string Name, string Title, string? Path = null);

internal static class AppPicker
{
    public static List<WindowedApp> Pickable(IEnumerable<WindowedApp> windows, int ownProcessId, Settings settings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return windows
            .Where(w => w.ProcessId != ownProcessId && !string.IsNullOrWhiteSpace(w.Title) && w.Name.Length > 0)
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Where(w => !settings.IsNeverDim(w.Name) && seen.Add(w.Name))
            .ToList();
    }

    /// <remarks>Reading an icon from a network share can hang the UI for seconds while the NAS sleeps, so these paths get a generic icon.</remarks>
    public static bool IsNetworkPath(string path, Func<string, DriveType>? driveType = null)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        if (path.Length < 2 || path[1] != ':') return false;
        try
        {
            return (driveType ?? (root => new DriveInfo(root).DriveType))(path[..1]) == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
