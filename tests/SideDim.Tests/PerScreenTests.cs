namespace SideDim.Tests;

public sealed class PerScreenTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static Monitor Screen(string id, int x, int y = 0) =>
        new(IntPtr.Zero, Device: @"\\.\DISPLAY" + id, Id: id, new Rectangle(x, y, 1920, 1080));

    private static readonly Monitor Left = Screen("L", -1920);
    private static readonly Monitor Main = Screen("M", 0);
    private static readonly Monitor Right = Screen("R", 1920);

    [Fact]
    public void Screens_without_overrides_follow_the_shared_levels()
    {
        var s = new Settings { BacklightLevel = 10, OverlayStrength = 70 };
        Assert.Equal(new ScreenLevels(true, 10, 70), s.LevelsFor("anything"));
    }

    [Fact]
    public void A_screen_can_override_one_level_and_inherit_the_other()
    {
        var s = new Settings { BacklightLevel = 10, OverlayStrength = 70 };
        s.MonitorFor("R", "DELL S2419HGF").OverlayStrength = 40;
        Assert.Equal(new ScreenLevels(true, 10, 40), s.LevelsFor("R"));
    }

    [Fact]
    public void Each_other_screen_is_dimmed_with_its_own_darkness()
    {
        var s = new Settings { BacklightLevel = 10, OverlayStrength = 70 };
        s.MonitorFor("R", null).OverlayStrength = 40;
        s.MonitorFor("R", null).BacklightLevel = 30;

        var targets = DimPolicy.Targets(s, [Left, Main, Right], keepBright: Main.Device);

        Assert.Equal(["L", "R"], targets.Select(t => t.Monitor.Id));
        Assert.Equal((70, 10), (targets[0].OverlayStrength, targets[0].BacklightLevel));
        Assert.Equal((40, 30), (targets[1].OverlayStrength, targets[1].BacklightLevel));
    }

    [Fact]
    public void A_never_dim_screen_is_left_alone()
    {
        var s = new Settings();
        s.MonitorFor("L", "chat screen").Dim = false;
        Assert.Equal(["R"], DimPolicy.Targets(s, [Left, Main, Right], Main.Device).Select(t => t.Monitor.Id));
    }

    [Fact]
    public void Nothing_is_dimmed_when_nothing_is_kept_bright() =>
        Assert.Empty(DimPolicy.Targets(new Settings(), [Left, Main, Right], keepBright: null));

    [Fact]
    public void Per_screen_settings_round_trip_and_unused_ones_are_dropped()
    {
        var path = _dir.File("settings.json");
        var s = new Settings();
        s.MonitorFor(@"\\?\DISPLAY#MSI3EA5#1", "MSI MAG401QR").OverlayStrength = 50;
        s.MonitorFor(@"\\?\DISPLAY#DELD0E3#2", "DELL S2419HGF").Dim = false;
        s.MonitorFor(@"\\?\DISPLAY#MSI3EA5#3", "MSI MAG401QR");
        s.Normalize().Save(path);

        var loaded = Settings.Load(path);

        Assert.Equal(2, loaded.Monitors.Count);
        Assert.Equal(50, loaded.LevelsFor(@"\\?\display#msi3ea5#1").OverlayStrength);
        Assert.False(loaded.LevelsFor(@"\\?\DISPLAY#DELD0E3#2").Dim);
        Assert.Equal("MSI MAG401QR", loaded.Monitors[@"\\?\DISPLAY#MSI3EA5#1"].Name);
    }

    [Fact]
    public void Hand_edited_per_screen_values_are_clamped()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """{ "Monitors": { "X": { "BacklightLevel": 300, "OverlayStrength": 100 }, "Y": null } }""");
        var s = Settings.Load(path);
        Assert.Equal(new ScreenLevels(true, 100, 95), s.LevelsFor("X"));
        Assert.False(s.Monitors.ContainsKey("Y"));
    }

    [Fact]
    public void Screens_are_numbered_left_to_right_and_identical_models_are_told_apart_by_number()
    {
        var names = new Dictionary<string, string> { ["L"] = "MSI MAG401QR", ["M"] = "MSI MAG401QR", ["R"] = "DELL S2419HGF" };

        var screens = ScreenList.Describe([Right, Main, Left], names);

        Assert.Equal(["1. MSI MAG401QR", "2. MSI MAG401QR (main)", "3. DELL S2419HGF"], screens.Select(s => s.Label));
    }

    [Fact]
    public void A_screen_without_a_reported_name_is_still_listed()
    {
        var screens = ScreenList.Describe([Main], new Dictionary<string, string>());
        Assert.Equal("1. Screen (main)", screens.Single().Label);
    }

    [Fact]
    public void Stacked_screens_are_numbered_top_to_bottom()
    {
        var top = Screen("T", 0, -1080);
        var screens = ScreenList.Describe([Main, top], new Dictionary<string, string>());
        Assert.Equal(["T", "M"], screens.Select(s => s.Monitor.Id));
    }
}
