namespace SideDim;

internal static class DimPolicy
{
    public static bool ShouldDim(Settings s, string process, bool isFullscreen)
    {
        var known = process.Length > 0;
        if (known && s.IsNeverDim(process)) return false;
        if (s.Trigger == DimTrigger.AnyWindow) return true;
        if (known && Settings.ListContains(s.Apps, process)) return true;
        return s.AlsoAnyFullscreen && isFullscreen;
    }

    /// <remarks>A maximized window with a title bar also covers a monitor that has no taskbar, but it is a normal window, not fullscreen.</remarks>
    public static bool IsFullscreen(Rectangle window, Rectangle monitor, bool isMaximized, bool hasCaption)
    {
        var covers = window.Left <= monitor.Left && window.Top <= monitor.Top
                  && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        return covers && !(isMaximized && hasCaption);
    }

    public static List<DimTarget> Targets(Settings s, IEnumerable<Monitor> monitors, string? keepBright)
    {
        if (keepBright is null) return [];
        var targets = new List<DimTarget>();
        foreach (var m in monitors)
        {
            if (m.Device == keepBright) continue;
            var levels = s.LevelsFor(m.Id);
            if (levels.Dim) targets.Add(new DimTarget(m, levels.OverlayStrength, levels.BacklightLevel));
        }
        return targets;
    }

    /// <summary>Spotlight is forced on with a single monitor, since there is nothing beside it to dim.</summary>
    public static bool SpotlightOn(Settings s, int monitorCount) => s.Spotlight || monitorCount == 1;

    public static bool BacklightAvailable(int monitorCount) => monitorCount > 1;

    public static Rectangle? SpotlightHole(bool spotlightOn, string? keptBright, string? windowMonitor, Rectangle window, bool isFullscreen)
    {
        if (!spotlightOn || keptBright is null || windowMonitor != keptBright) return null;
        if (isFullscreen || window.IsEmpty) return null;
        return window;
    }

    public static Rectangle ToOverlayCoordinates(Rectangle hole, Rectangle monitor) =>
        hole with { X = hole.X - monitor.X, Y = hole.Y - monitor.Y };
}
