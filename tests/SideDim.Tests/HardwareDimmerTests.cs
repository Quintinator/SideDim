namespace SideDim.Tests;

/// <summary>The threaded wrapper that keeps slow DDC/CI calls off the UI thread.</summary>
public class HardwareDimmerTests
{
    // Device and Id differ on purpose: saved brightness must be keyed by the stable Id, not \\.\DISPLAYn.
    private static Monitor Screen(string id) => new(IntPtr.Zero, Device: @"\\.\DISPLAY" + id, Id: id, Rectangle.Empty);

    [Fact]
    public void Applies_on_the_worker_and_restores_on_dispose()
    {
        var t = new Timeline();
        var monitors = new FakeMonitors(t).Add("D1", 70).Add("D2", 100);
        var level = 10;
        var store = new FakeStore(t);
        var dimmer = new HardwareDimmer(new BacklightController(monitors, store), () => level);

        dimmer.Apply([new DimTarget(Screen("D1"))]);
        Assert.True(dimmer.Flush());
        Assert.Equal(10u, monitors.Brightness["D1"]);
        Assert.Equal(["D1"], store.Saved.Keys);
        Assert.Equal(100u, monitors.Brightness["D2"]);

        dimmer.Dispose();
        Assert.Equal(70u, monitors.Brightness["D1"]);
    }

    [Fact]
    public void Level_is_read_when_applying_not_when_created()
    {
        var t = new Timeline();
        var monitors = new FakeMonitors(t).Add("D1", 70);
        var level = 10;
        using var dimmer = new HardwareDimmer(new BacklightController(monitors, new FakeStore(t)), () => level);

        level = 40;
        dimmer.Apply([new DimTarget(Screen("D1"))]);
        Assert.True(dimmer.Flush());
        Assert.Equal(40u, monitors.Brightness["D1"]);
    }

    [Fact]
    public void A_failing_monitor_call_does_not_stop_later_work()
    {
        var t = new Timeline();
        var monitors = new ThrowOnceMonitors(new FakeMonitors(t).Add("D1", 70));
        using var dimmer = new HardwareDimmer(new BacklightController(monitors, new FakeStore(t)), () => 10);

        dimmer.Apply([new DimTarget(Screen("D1"))]); // throws inside the worker
        dimmer.Apply([new DimTarget(Screen("D1"))]);
        Assert.True(dimmer.Flush());
        Assert.Equal(10u, monitors.Inner.Brightness["D1"]);
    }

    [Fact]
    public void Dispose_twice_is_harmless()
    {
        var t = new Timeline();
        var dimmer = new HardwareDimmer(new BacklightController(new FakeMonitors(t), new FakeStore(t)), () => 10);
        dimmer.Dispose();
        dimmer.Dispose();
    }

    private sealed class ThrowOnceMonitors(FakeMonitors inner) : IBrightnessDevice
    {
        private bool _thrown;
        public FakeMonitors Inner => inner;
        public bool IsConnected(string device) => inner.IsConnected(device);
        public uint? Read(string device)
        {
            if (!_thrown) { _thrown = true; throw new InvalidOperationException("I2C bus hiccup"); }
            return inner.Read(device);
        }
        public bool Write(string device, uint value) => inner.Write(device, value);
    }
}
