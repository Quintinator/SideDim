namespace SideDim.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly TempFolder _dir = new();
    private string FilePath => _dir.File("settings.json");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Missing_file_gives_defaults_and_marks_first_run()
    {
        var s = Settings.Load(FilePath);
        Assert.True(s.IsFirstRun);
        Assert.True(s.Enabled);
        Assert.Equal(DimMode.Overlay, s.Mode);
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Round_trips_every_setting()
    {
        var original = new Settings
        {
            Enabled = false,
            Trigger = DimTrigger.AnyWindow,
            Apps = [@"C:\Games\cs2.exe"],
            AlsoAnyFullscreen = false,
            Mode = DimMode.Both,
            BacklightLevel = 25,
            OverlayStrength = 55,
            Spotlight = true,
            DimDelayMs = 1200,
            RestoreDelayMs = 100,
            FadeMs = 0,
            ToggleHotkey = "Ctrl+Shift+F9",
        };
        original.Save(FilePath);

        var loaded = Settings.Load(FilePath);

        Assert.False(loaded.IsFirstRun);
        Assert.Equal(original.Enabled, loaded.Enabled);
        Assert.Equal(original.Trigger, loaded.Trigger);
        Assert.Equal(original.Apps, loaded.Apps);
        Assert.Equal(original.AlsoAnyFullscreen, loaded.AlsoAnyFullscreen);
        Assert.Equal(original.Mode, loaded.Mode);
        Assert.Equal(original.BacklightLevel, loaded.BacklightLevel);
        Assert.Equal(original.OverlayStrength, loaded.OverlayStrength);
        Assert.Equal(original.Spotlight, loaded.Spotlight);
        Assert.Equal(original.DimDelayMs, loaded.DimDelayMs);
        Assert.Equal(original.RestoreDelayMs, loaded.RestoreDelayMs);
        Assert.Equal(original.FadeMs, loaded.FadeMs);
        Assert.Equal(original.ToggleHotkey, loaded.ToggleHotkey);
    }

    [Fact]
    public void Enums_are_stored_as_readable_text()
    {
        new Settings { Mode = DimMode.Hardware, Trigger = DimTrigger.AnyWindow }.Save(FilePath);
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"Hardware\"", json);
        Assert.Contains("\"AnyWindow\"", json);
    }

    [Fact]
    public void Corrupt_file_is_kept_as_a_backup_instead_of_being_overwritten()
    {
        File.WriteAllText(FilePath, "{ this is not json");
        var s = Settings.Load(FilePath);

        Assert.Equal(DimMode.Overlay, s.Mode);
        var backup = Directory.GetFiles(_dir.Path, "settings.json.broken*").Single();
        Assert.Equal("{ this is not json", File.ReadAllText(backup));
    }

    [Fact]
    public void Out_of_range_values_from_hand_edits_are_clamped()
    {
        File.WriteAllText(FilePath, """
            { "BacklightLevel": 250, "OverlayStrength": -5, "DimDelayMs": -100, "RestoreDelayMs": 999999, "FadeMs": -1 }
            """);
        var s = Settings.Load(FilePath);

        Assert.Equal(100, s.BacklightLevel);
        Assert.Equal(0, s.OverlayStrength);
        Assert.Equal(0, s.DimDelayMs);
        Assert.Equal(Settings.MaxDelayMs, s.RestoreDelayMs);
        Assert.Equal(0, s.FadeMs);
    }

    [Fact]
    public void Upper_limits_are_exactly_what_the_readme_promises()
    {
        File.WriteAllText(FilePath, """{ "BacklightLevel": 101, "OverlayStrength": 100, "DimDelayMs": 99999, "FadeMs": 99999 }""");
        var s = Settings.Load(FilePath);
        Assert.Equal(100, s.BacklightLevel);
        Assert.Equal(95, s.OverlayStrength);  // never fully black: a stuck overlay must not hide a screen
        Assert.Equal(10_000, s.DimDelayMs);
        Assert.Equal(2_000, s.FadeMs);
    }

    [Fact]
    public void Null_lists_and_hotkey_fall_back_to_defaults()
    {
        File.WriteAllText(FilePath, """{ "Apps": null, "NeverDimFor": null, "ToggleHotkey": null }""");
        var s = Settings.Load(FilePath);

        Assert.Empty(s.Apps);
        Assert.Empty(s.NeverDimFor);
        Assert.True(s.IsNeverDim("explorer")); // built-ins still apply
        Assert.Equal(Settings.DefaultHotkey, s.ToggleHotkey);
    }

    [Fact]
    public void Unknown_mode_text_falls_back_without_losing_other_settings()
    {
        File.WriteAllText(FilePath, """{ "Mode": "Laser", "BacklightLevel": 42 }""");
        var s = Settings.Load(FilePath);
        Assert.Equal(DimMode.Overlay, s.Mode);
        Assert.Equal(42, s.BacklightLevel);
    }

    [Fact]
    public void Built_in_never_dim_entries_saved_by_older_versions_are_dropped_but_user_entries_kept()
    {
        File.WriteAllText(FilePath, """{ "NeverDimFor": ["explorer", "SnippingTool", "obs64"] }""");
        var s = Settings.Load(FilePath);
        Assert.Equal(["obs64"], s.NeverDimFor);
        Assert.True(s.IsNeverDim("explorer"));
        Assert.True(s.IsNeverDim("OBS64"));
    }

    [Fact]
    public void A_saved_hotkey_is_kept_even_though_the_default_changed()
    {
        File.WriteAllText(FilePath, """{ "ToggleHotkey": "Ctrl+Alt+D" }""");
        Assert.Equal("Ctrl+Alt+D", Settings.Load(FilePath).ToggleHotkey);
    }

    [Fact]
    public void Hand_edits_with_comments_and_trailing_commas_are_accepted()
    {
        File.WriteAllText(FilePath, """
            {
              // darker please
              "OverlayStrength": 90,
              "Apps": ["cs2",],
            }
            """);
        var s = Settings.Load(FilePath);
        Assert.Equal(90, s.OverlayStrength);
        Assert.Equal(["cs2"], s.Apps);
    }

    [Fact]
    public void A_locked_file_is_left_alone_and_defaults_are_used_for_now()
    {
        File.WriteAllText(FilePath, """{ "OverlayStrength": 90 }""");
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var s = Settings.Load(FilePath);
            Assert.Equal(70, s.OverlayStrength);
            Assert.False(s.IsFirstRun);
        }
        Assert.Empty(Directory.GetFiles(_dir.Path, "settings.json.broken*"));
        Assert.Equal(90, Settings.Load(FilePath).OverlayStrength); // nothing was overwritten
    }

    [Fact]
    public void Changes_made_after_a_failed_load_never_silently_replace_the_unread_file()
    {
        File.WriteAllText(FilePath, """{ "Apps": ["cs2"] }""");
        Settings s;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            s = Settings.Load(FilePath);
            s.Enabled = false;
            Assert.False(s.Save(FilePath)); // still locked: refuse rather than clobber
        }

        Assert.True(s.Save(FilePath));    // unlocked: the unread file is set aside first
        var aside = Directory.GetFiles(_dir.Path, "settings.json.unread-*").Single();
        Assert.Contains("cs2", File.ReadAllText(aside));
    }

    [Fact]
    public void Unknown_properties_are_ignored()
    {
        File.WriteAllText(FilePath, """{ "SomethingFromAFutureVersion": true, "Spotlight": true }""");
        Assert.True(Settings.Load(FilePath).Spotlight);
    }

    [Fact]
    public void Adding_an_app_twice_by_a_different_path_is_a_no_op()
    {
        var s = new Settings();
        Assert.True(s.AddApp(@"C:\Games\cs2.exe"));
        Assert.False(s.AddApp(@"D:\Steam\cs2.exe"));
        Assert.False(s.AddApp("CS2"));
        Assert.Single(s.Apps);
    }

    [Fact]
    public void Removing_an_app_matches_by_name()
    {
        var s = new Settings { Apps = [@"C:\Games\cs2.exe", "eldenring"] };
        Assert.True(s.RemoveApp("CS2"));
        Assert.Equal(["eldenring"], s.Apps);
    }

    [Fact]
    public void Duplicate_apps_in_the_file_are_collapsed()
    {
        File.WriteAllText(FilePath, """{ "Apps": ["cs2", "C:\\Games\\cs2.exe", "eldenring"] }""");
        Assert.Equal(2, Settings.Load(FilePath).Apps.Count);
    }

    [Theory]
    [InlineData(@"C:\Games\cs2.exe", "cs2")]
    [InlineData("cs2.exe", "cs2")]
    [InlineData("  cs2  ", "cs2")]
    [InlineData("Code - Insiders.exe", "Code - Insiders")]
    [InlineData("Battle.net", "Battle.net")]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", "Battle.net")]
    [InlineData("Minecraft.Windows", "Minecraft.Windows")]
    [InlineData(" CS2.EXE ", "CS2")]
    public void AppName_normalizes_entries(string entry, string expected) =>
        Assert.Equal(expected, Settings.AppName(entry));
}
