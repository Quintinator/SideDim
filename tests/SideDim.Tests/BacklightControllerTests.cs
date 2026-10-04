namespace SideDim.Tests;

public class BacklightControllerTests
{
    private readonly Timeline _timeline = new();
    private readonly FakeMonitors _monitors;
    private readonly FakeStore _store;

    public BacklightControllerTests()
    {
        _monitors = new FakeMonitors(_timeline).Add("D1", 100).Add("D2", 60).Add("D3", 5);
        _store = new FakeStore(_timeline);
    }

    private BacklightController Create() => new(_monitors, _store);

    [Fact]
    public void Dims_to_the_chosen_level()
    {
        var c = Create();
        c.Apply(["D1"], level: 10);
        Assert.Equal(10u, _monitors.Brightness["D1"]);
    }

    [Fact]
    public void Restores_the_previous_brightness_not_full()
    {
        var c = Create();
        c.Apply(["D2"], level: 10);
        c.Apply([], level: 10);
        Assert.Equal(60u, _monitors.Brightness["D2"]);
    }

    [Fact]
    public void Saves_the_original_before_touching_the_monitor()
    {
        var c = Create();
        c.Apply(["D1"], level: 10);
        var save = _timeline.Events.FindIndex(e => e.StartsWith("save") && e.Contains("D1=100"));
        var write = _timeline.Events.IndexOf("write D1=10");
        Assert.True(save >= 0 && write > save, string.Join(" | ", _timeline.Events));
    }

    [Fact]
    public void Never_brightens_a_screen_that_is_already_darker()
    {
        var c = Create();
        c.Apply(["D3"], level: 10);
        Assert.Equal(5u, _monitors.Brightness["D3"]);
        Assert.DoesNotContain("write D3=10", _timeline.Events);
    }

    [Fact]
    public void Repeated_apply_does_not_rewrite_the_monitor()
    {
        var c = Create();
        c.Apply(["D1"], level: 10);
        var writes = _monitors.Writes;
        c.Apply(["D1"], level: 10);
        c.Apply(["D1"], level: 10);
        Assert.Equal(writes, _monitors.Writes);
    }

    [Fact]
    public void Changing_the_level_while_dimmed_rewrites_without_losing_the_original()
    {
        var c = Create();
        c.Apply(["D1"], level: 10);
        c.Apply(["D1"], level: 30);
        Assert.Equal(30u, _monitors.Brightness["D1"]);
        c.Apply([], level: 30);
        Assert.Equal(100u, _monitors.Brightness["D1"]);
    }

    [Fact]
    public void Moving_focus_swaps_which_screens_are_dimmed()
    {
        var c = Create();
        c.Apply(["D1", "D2"], level: 10);
        c.Apply(["D2", "D3"], level: 10);
        Assert.Equal(100u, _monitors.Brightness["D1"]);
        Assert.Equal(10u, _monitors.Brightness["D2"]);
        Assert.Equal(5u, _monitors.Brightness["D3"]);
    }

    [Fact]
    public void Monitor_without_ddc_is_skipped_and_not_remembered()
    {
        _monitors.IgnoresDdc.Add("D1");
        var c = Create();
        c.Apply(["D1", "D2"], level: 10);
        Assert.Equal(10u, _monitors.Brightness["D2"]);
        Assert.False(_store.Saved.ContainsKey("D1"));
    }

    [Fact]
    public void Monitor_without_ddc_is_reported_each_time_it_is_skipped()
    {
        _monitors.IgnoresDdc.Add("D1");
        var reported = new List<string>();
        var c = new BacklightController(_monitors, _store, reported.Add);
        c.Apply(["D1"], level: 10);
        c.Apply([], level: 10);
        c.Apply(["D1"], level: 10);
        Assert.Equal(["D1", "D1"], reported); // every attempt is reported; the UI decides what to show

        _monitors.IgnoresDdc.Remove("D1"); // DDC/CI switched on in the monitor menu
        c.Apply(["D1"], level: 10);
        Assert.Equal(10u, _monitors.Brightness["D1"]);

        c.Apply([], level: 10);
        _monitors.IgnoresDdc.Add("D1");    // off again, or lost after sleep
        c.Apply(["D1"], level: 10);
        Assert.Equal(["D1", "D1", "D1"], reported);
    }

    [Fact]
    public void Store_is_empty_once_everything_is_restored()
    {
        var c = Create();
        c.Apply(["D1", "D2"], level: 10);
        Assert.Equal(2, _store.Saved.Count);
        c.RestoreAll();
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Leftovers_from_a_crash_are_restored_on_startup()
    {
        // A previous run dimmed D1 and D2 and then died.
        _store.Save(new Dictionary<string, uint> { ["D1"] = 80, ["D2"] = 60 });
        _monitors.Brightness["D1"] = 10;
        _monitors.Brightness["D2"] = 10;

        Create().RestoreAll();

        Assert.Equal(80u, _monitors.Brightness["D1"]);
        Assert.Equal(60u, _monitors.Brightness["D2"]);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Leftover_original_wins_over_the_current_dimmed_value()
    {
        // After a crash the monitor reads 10, but 80 is what the user had.
        _store.Save(new Dictionary<string, uint> { ["D1"] = 80 });
        _monitors.Brightness["D1"] = 10;

        var c = Create();
        c.Apply(["D1"], level: 10);
        c.Apply([], level: 10);

        Assert.Equal(80u, _monitors.Brightness["D1"]);
    }

    [Fact]
    public void A_monitor_is_not_touched_when_its_brightness_cannot_be_backed_up()
    {
        _store.FailSaves = true;
        var c = Create();
        c.Apply(["D1"], level: 10);
        Assert.Equal(100u, _monitors.Brightness["D1"]);
        Assert.Equal(0, _monitors.Writes);
        Assert.Empty(c.Originals);
    }

    [Fact]
    public void Redimming_after_a_skipped_restore_writes_again()
    {
        var c = Create();
        c.Apply(["D2"], level: 10);
        _monitors.Connected.Remove("D2");
        c.Apply([], level: 10);              // restore skipped: monitor gone

        _monitors.Connected.Add("D2");
        _monitors.Brightness["D2"] = 50;    // user changed it in the monitor menu meanwhile
        c.Apply(["D2"], level: 10);
        Assert.Equal(10u, _monitors.Brightness["D2"]);
        c.Apply([], level: 10);
        Assert.Equal(60u, _monitors.Brightness["D2"]); // still the original from before the first dim
    }

    [Fact]
    public void Unplugged_monitor_keeps_its_original_until_it_comes_back()
    {
        var c = Create();
        c.Apply(["D2"], level: 10);

        _monitors.Connected.Remove("D2");
        c.Apply([], level: 10);
        Assert.Equal(60u, _store.Saved["D2"]); // still remembered

        _monitors.Connected.Add("D2");
        c.RestoreAll();
        Assert.Equal(60u, _monitors.Brightness["D2"]);
        Assert.Empty(_store.Saved);
    }
}
