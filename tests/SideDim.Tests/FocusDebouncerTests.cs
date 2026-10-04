namespace SideDim.Tests;

public class FocusDebouncerTests
{
    private static readonly TimeSpan DimDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(200);
    private static TimeSpan At(int ms) => TimeSpan.FromMilliseconds(1_000_000 + ms);

    [Fact]
    public void Waits_for_the_dim_delay_before_dimming()
    {
        var d = new FocusDebouncer();
        Assert.False(d.Update("D1", At(0), DimDelay, RestoreDelay));
        Assert.False(d.Update("D1", At(500), DimDelay, RestoreDelay));
        Assert.True(d.Update("D1", At(800), DimDelay, RestoreDelay));
        Assert.Equal("D1", d.Applied);
    }

    [Fact]
    public void Quick_alt_tab_never_dims()
    {
        var d = new FocusDebouncer();
        d.Update("D1", At(0), DimDelay, RestoreDelay);
        d.Update(null, At(300), DimDelay, RestoreDelay);
        d.Update("D1", At(600), DimDelay, RestoreDelay);
        Assert.False(d.Update("D1", At(1000), DimDelay, RestoreDelay));
        Assert.Null(d.Applied);
    }

    [Fact]
    public void Restores_after_the_shorter_restore_delay()
    {
        var d = new FocusDebouncer();
        d.ForceApply("D1");
        Assert.False(d.Update(null, At(0), DimDelay, RestoreDelay));
        Assert.True(d.Update(null, At(200), DimDelay, RestoreDelay));
        Assert.Null(d.Applied);
    }

    [Fact]
    public void Switching_to_another_monitor_restarts_the_wait()
    {
        var d = new FocusDebouncer();
        d.ForceApply("D1");
        d.Update("D2", At(0), DimDelay, RestoreDelay);
        Assert.False(d.Update("D2", At(799), DimDelay, RestoreDelay));
        Assert.True(d.Update("D2", At(800), DimDelay, RestoreDelay));
        Assert.Equal("D2", d.Applied);
    }

    [Fact]
    public void Nothing_to_do_when_desired_already_applied()
    {
        var d = new FocusDebouncer();
        Assert.False(d.Update(null, At(0), DimDelay, RestoreDelay));
        Assert.False(d.Update(null, At(5000), DimDelay, RestoreDelay));
    }

    [Fact]
    public void Zero_delay_applies_immediately()
    {
        var d = new FocusDebouncer();
        Assert.True(d.Update("D1", At(0), TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void Timestamp_going_backwards_does_not_get_stuck()
    {
        var d = new FocusDebouncer();
        d.Update("D1", At(1000), DimDelay, RestoreDelay);
        d.Update("D1", At(0), DimDelay, RestoreDelay);
        Assert.True(d.Update("D1", At(800), DimDelay, RestoreDelay));
    }
}
