namespace SideDim.Tests;

public class HardwareDimmerTests
{
    private static Monitor Screen(string id) => new(IntPtr.Zero, Device: @"\\.\DISPLAY" + id, Id: id, Rectangle.Empty);

    private static DimTarget Target(string id, int backlight) => new(Screen(id), OverlayStrength: 70, BacklightLevel: backlight);

    [Fact]
    public void Applies_on_the_worker_and_restores_on_dispose()
    {
        var t = new Timeline();
        var monitors = new FakeMonitors(t).Add("D1", 70).Add("D2", 100);
        var store = new FakeStore(t);
        var dimmer = new HardwareDimmer(new BacklightController(monitors, store));

        dimmer.Apply([Target("D1", 10)]);
        Assert.True(dimmer.Flush());
        Assert.Equal(10u, monitors.Brightness["D1"]);
        Assert.Equal(100u, monitors.Brightness["D2"]);
        Assert.Equal(["D1"], store.Saved.Keys);

        dimmer.Dispose();
        Assert.Equal(70u, monitors.Brightness["D1"]);
    }

    [Fact]
    public void Each_screen_gets_its_own_level()
    {
        var t = new Timeline();
        var monitors = new FakeMonitors(t).Add("D1", 100).Add("D2", 100);
        using var dimmer = new HardwareDimmer(new BacklightController(monitors, new FakeStore(t)));

        dimmer.Apply([Target("D1", 10), Target("D2", 40)]);
        Assert.True(dimmer.Flush());
        Assert.Equal(10u, monitors.Brightness["D1"]);
        Assert.Equal(40u, monitors.Brightness["D2"]);
    }

    [Fact]
    public void A_failing_monitor_call_does_not_stop_later_work()
    {
        var t = new Timeline();
        var monitors = new ThrowOnceMonitors(new FakeMonitors(t).Add("D1", 70));
        using var dimmer = new HardwareDimmer(new BacklightController(monitors, new FakeStore(t)));

        dimmer.Apply([Target("D1", 10)]);
        dimmer.Apply([Target("D1", 10)]);
        Assert.True(dimmer.Flush());
        Assert.Equal(10u, monitors.Inner.Brightness["D1"]);
    }

    [Fact]
    public void Dispose_twice_is_harmless()
    {
        var t = new Timeline();
        var dimmer = new HardwareDimmer(new BacklightController(new FakeMonitors(t), new FakeStore(t)));
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
