namespace SideDim;

/// <summary>A process with a visible main window, as offered by "Pick running app".</summary>
internal readonly record struct WindowedApp(int ProcessId, string Name, string Title, string? Path = null);

/// <summary>Pure helpers behind the app list, kept out of the form so they can be tested.</summary>
internal static class AppPicker
{
    /// <summary>
    /// The apps worth offering: not SideDim itself, not windows without a title, not apps that can never
    /// trigger dimming, and each process name once, sorted by name.
    /// </summary>
    public static List<WindowedApp> Pickable(IEnumerable<WindowedApp> windows, int ownProcessId, Settings settings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return windows
            .Where(w => w.ProcessId != ownProcessId && !string.IsNullOrWhiteSpace(w.Title) && w.Name.Length > 0)
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Where(w => !settings.IsNeverDim(w.Name) && seen.Add(w.Name))
            .ToList();
    }

    /// <summary>
    /// True for paths on a network share. Reading an icon from one can hang the UI for seconds when the
    /// NAS is asleep, so the app list shows a generic icon for those instead.
    /// </summary>
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
