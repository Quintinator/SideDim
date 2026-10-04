namespace SideDim;

/// <summary>The rules for when and where to dim. Pure functions, so they're easy to test.</summary>
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

    /// <summary>
    /// A window that covers its whole monitor. A maximized window with a title bar also covers a monitor
    /// that has no taskbar, but that's a normal app, not a game.
    /// </summary>
    public static bool IsFullscreen(Rectangle window, Rectangle monitor, bool isMaximized, bool hasCaption)
    {
        var covers = window.Left <= monitor.Left && window.Top <= monitor.Top
                  && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        return covers && !(isMaximized && hasCaption);
    }

    /// <summary>With one monitor there's nothing beside it to dim, so the spotlight is the whole point.</summary>
    public static bool SpotlightOn(Settings s, int monitorCount) => s.Spotlight || monitorCount == 1;

    /// <summary>Backlight dimming only ever touches the other monitors, so it needs at least two.</summary>
    public static bool BacklightAvailable(int monitorCount) => monitorCount > 1;

    /// <summary>
    /// The area to leave lit on the focused screen when spotlight is on, or null for no spotlight.
    /// A fullscreen window needs no spotlight: there's nothing around it to darken.
    /// </summary>
    public static Rectangle? SpotlightHole(bool spotlightOn, string? keptBright, string? windowMonitor, Rectangle window, bool isFullscreen)
    {
        if (!spotlightOn || keptBright is null || windowMonitor != keptBright) return null;
        if (isFullscreen || window.IsEmpty) return null;
        return window;
    }

    /// <summary>Screen coordinates to coordinates inside an overlay that covers <paramref name="monitor"/>.</summary>
    public static Rectangle ToOverlayCoordinates(Rectangle hole, Rectangle monitor) =>
        hole with { X = hole.X - monitor.X, Y = hole.Y - monitor.Y };
}
