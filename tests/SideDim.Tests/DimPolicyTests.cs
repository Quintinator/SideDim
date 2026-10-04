namespace SideDim.Tests;

public class DimPolicyTests
{
    private static Settings AppsMode(params string[] apps) => new() { Trigger = DimTrigger.SelectedApps, Apps = [.. apps], AlsoAnyFullscreen = false };

    [Theory]
    [InlineData(@"C:\Games\Elden Ring\eldenring.exe")]
    [InlineData("eldenring.exe")]
    [InlineData("eldenring")]
    [InlineData("ELDENRING")]
    public void Listed_app_dims_however_it_was_entered(string entry) =>
        Assert.True(DimPolicy.ShouldDim(AppsMode(entry), "eldenring", isFullscreen: false));

    [Fact]
    public void Unlisted_windowed_app_does_not_dim() =>
        Assert.False(DimPolicy.ShouldDim(AppsMode("eldenring"), "chrome", isFullscreen: false));

    [Fact]
    public void Fullscreen_app_dims_only_when_that_option_is_on()
    {
        var s = AppsMode();
        Assert.False(DimPolicy.ShouldDim(s, "vlc", isFullscreen: true));
        s.AlsoAnyFullscreen = true;
        Assert.True(DimPolicy.ShouldDim(s, "vlc", isFullscreen: true));
    }

    [Fact]
    public void Any_window_mode_dims_for_everything()
    {
        var s = new Settings { Trigger = DimTrigger.AnyWindow };
        Assert.True(DimPolicy.ShouldDim(s, "notepad", isFullscreen: false));
    }

    [Fact]
    public void Never_list_beats_every_other_rule()
    {
        var s = new Settings { Trigger = DimTrigger.AnyWindow, NeverDimFor = ["explorer"], Apps = ["explorer"] };
        Assert.False(DimPolicy.ShouldDim(s, "Explorer", isFullscreen: true));
    }

    [Fact]
    public void Unknown_process_name_never_matches_an_empty_entry()
    {
        var s = AppsMode("", "  ");
        Assert.False(DimPolicy.ShouldDim(s, "", isFullscreen: false));
    }

    private static readonly Rectangle Primary = new(0, 0, 3440, 1440);
    private static readonly Rectangle LeftPortrait = new(-1080, -1037, 1080, 1920);

    [Fact]
    public void Window_exactly_covering_the_monitor_is_fullscreen() =>
        Assert.True(DimPolicy.IsFullscreen(Primary, Primary, isMaximized: false, hasCaption: false));

    [Fact]
    public void Window_larger_than_the_monitor_is_fullscreen() =>
        Assert.True(DimPolicy.IsFullscreen(Rectangle.Inflate(Primary, 8, 8), Primary, isMaximized: false, hasCaption: false));

    [Fact]
    public void Fullscreen_works_on_monitors_with_negative_coordinates() =>
        Assert.True(DimPolicy.IsFullscreen(LeftPortrait, LeftPortrait, isMaximized: false, hasCaption: false));

    [Fact]
    public void Normal_window_is_not_fullscreen() =>
        Assert.False(DimPolicy.IsFullscreen(new Rectangle(100, 100, 1600, 900), Primary, isMaximized: false, hasCaption: true));

    [Fact]
    public void Maximized_window_with_title_bar_is_not_a_game_even_without_a_taskbar() =>
        Assert.False(DimPolicy.IsFullscreen(Primary, Primary, isMaximized: true, hasCaption: true));

    [Fact]
    public void Maximized_borderless_game_window_is_fullscreen() =>
        Assert.True(DimPolicy.IsFullscreen(Primary, Primary, isMaximized: true, hasCaption: false));

    [Fact]
    public void Spotlight_cuts_a_hole_for_the_focused_window()
    {
        var s = new Settings { Spotlight = true };
        var window = new Rectangle(200, 100, 800, 600);
        Assert.Equal(window, DimPolicy.SpotlightHole(s, keptBright: "D1", windowMonitor: "D1", window, isFullscreen: false));
    }

    [Fact]
    public void No_spotlight_when_turned_off_fullscreen_or_on_another_screen()
    {
        var window = new Rectangle(200, 100, 800, 600);
        Assert.Null(DimPolicy.SpotlightHole(new Settings { Spotlight = false }, "D1", "D1", window, false));
        Assert.Null(DimPolicy.SpotlightHole(new Settings { Spotlight = true }, "D1", "D1", window, isFullscreen: true));
        Assert.Null(DimPolicy.SpotlightHole(new Settings { Spotlight = true }, "D1", "D2", window, false));
        Assert.Null(DimPolicy.SpotlightHole(new Settings { Spotlight = true }, null, "D1", window, false));
        Assert.Null(DimPolicy.SpotlightHole(new Settings { Spotlight = true }, "D1", "D1", Rectangle.Empty, false));
    }

    [Fact]
    public void Hole_is_converted_to_overlay_coordinates_on_offset_monitors()
    {
        var hole = new Rectangle(-1000, -900, 400, 300);
        Assert.Equal(new Rectangle(80, 137, 400, 300), DimPolicy.ToOverlayCoordinates(hole, LeftPortrait));
    }
}
