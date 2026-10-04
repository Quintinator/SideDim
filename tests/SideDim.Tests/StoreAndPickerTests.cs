namespace SideDim.Tests;

public sealed class FileBrightnessStoreTests : IDisposable
{
    private readonly TempFolder _dir = new();
    private string StatePath => _dir.File("hardware-state.json");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Round_trips_and_deletes_the_file_when_nothing_is_dimmed()
    {
        var store = new FileBrightnessStore(StatePath);
        Assert.True(store.Save(new Dictionary<string, uint> { [@"\\?\DISPLAY#MSI3CA9#1"] = 80 }));
        Assert.Equal(80u, store.Load()[@"\\?\DISPLAY#MSI3CA9#1"]);

        Assert.True(store.Save(new Dictionary<string, uint>()));
        Assert.False(File.Exists(StatePath));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void A_corrupt_file_is_set_aside_instead_of_overwritten()
    {
        File.WriteAllText(StatePath, "{ not json");
        Assert.Empty(new FileBrightnessStore(StatePath).Load());
        Assert.False(File.Exists(StatePath));
        Assert.Equal("{ not json", File.ReadAllText(StatePath + ".corrupt"));
    }

    [Fact]
    public void A_backup_left_by_version_0_1_0_in_the_roaming_folder_is_moved_and_restored()
    {
        var legacy = _dir.File("roaming-hardware-state.json");
        File.WriteAllText(legacy, """{ "\\\\.\\DISPLAY2": 80 }""");

        var loaded = new FileBrightnessStore(StatePath, legacy).Load();

        Assert.Equal(80u, loaded[@"\\.\DISPLAY2"]);
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(StatePath));
    }

    [Fact]
    public void An_unreadable_backup_is_never_overwritten()
    {
        File.WriteAllText(StatePath, """{ "D2": 80 }""");
        var store = new FileBrightnessStore(StatePath);
        using (new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(store.Load());
        }
        Assert.False(store.Save(new Dictionary<string, uint> { ["D1"] = 100 }));
        Assert.Equal(80u, new FileBrightnessStore(StatePath).Load()["D2"]);
    }

    [Fact]
    public void A_corrupt_backup_that_cannot_be_moved_aside_is_never_overwritten()
    {
        File.WriteAllText(StatePath, "{ not json");
        var store = new FileBrightnessStore(StatePath);
        using (new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Empty(store.Load());
        }
        Assert.False(store.Save(new Dictionary<string, uint> { ["D1"] = 100 }));
        Assert.Equal("{ not json", File.ReadAllText(StatePath));
    }

    [Fact]
    public void Save_reports_failure_instead_of_throwing()
    {
        Directory.CreateDirectory(StatePath);
        Assert.False(new FileBrightnessStore(StatePath).Save(new Dictionary<string, uint> { ["D1"] = 50 }));
    }
}

public class AppPickerTests
{
    private const int Own = 4242;

    [Fact]
    public void Offers_each_windowed_app_once_sorted_without_ourselves_or_shell_apps()
    {
        WindowedApp[] windows =
        [
            new(1, "zoom", "Meeting"),
            new(Own, "SideDim", "SideDim"),
            new(2, "explorer", "Downloads"),
            new(3, "chrome", "YouTube"),
            new(4, "chrome", "Gmail"),
            new(5, "eldenring", "ELDEN RING"),
            new(6, "TextInputHost", ""),
        ];

        var names = AppPicker.Pickable(windows, Own, new Settings()).Select(a => a.Name);

        Assert.Equal(["chrome", "eldenring", "zoom"], names);
    }

    [Fact]
    public void User_never_dim_entries_are_not_offered() =>
        Assert.Empty(AppPicker.Pickable([new(1, "obs64", "OBS")], Own, new Settings { NeverDimFor = ["obs64"] }));

    [Theory]
    [InlineData(@"\\nas\games\game.exe", true)]
    [InlineData(@"N:\games\game.exe", true)]
    [InlineData(@"C:\Games\game.exe", false)]
    [InlineData("game", false)]
    public void Network_paths_are_recognised(string path, bool expected) =>
        Assert.Equal(expected, AppPicker.IsNetworkPath(path, root => root == "N" ? DriveType.Network : DriveType.Fixed));
}

public class AutostartTests
{
    [Theory]
    [InlineData(false, null, "0.2.0", true)]
    [InlineData(true, "0.1.0", "0.2.0", true)]
    [InlineData(true, "0.3.0", "0.2.0", false)]
    [InlineData(true, "0.2.0", "0.2.0", false)]
    [InlineData(true, null, "0.2.0", false)]
    public void Repoints_only_from_a_missing_or_older_SideDim(bool exists, string? version, string current, bool expected) =>
        Assert.Equal(expected, Autostart.ShouldRepoint(exists, version, current));

    [Theory]
    [InlineData("\"C:\\Tools\\SideDim.exe\"", @"C:\Tools\SideDim.exe", true)]
    [InlineData(@"c:\tools\sidedim.exe", @"C:\Tools\SideDim.exe", true)]
    [InlineData("\"D:\\Old\\SideDim.exe\"", @"C:\Tools\SideDim.exe", false)]
    [InlineData(null, @"C:\Tools\SideDim.exe", false)]
    public void Only_counts_as_on_when_it_points_at_this_exe(string? stored, string exe, bool expected) =>
        Assert.Equal(expected, Autostart.PointsAt(stored, exe));
}
