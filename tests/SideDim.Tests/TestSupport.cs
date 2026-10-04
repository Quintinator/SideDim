using System.Runtime.CompilerServices;

namespace SideDim.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void SilenceLog() => Log.Sink = _ => { };
}

internal sealed class Timeline
{
    public List<string> Events { get; } = [];
}

internal sealed class FakeMonitors(Timeline timeline) : IBrightnessDevice
{
    public Dictionary<string, uint> Brightness { get; } = [];
    public HashSet<string> Connected { get; } = [];
    public HashSet<string> IgnoresDdc { get; } = [];
    public int Writes { get; private set; }

    public FakeMonitors Add(string device, uint brightness)
    {
        Connected.Add(device);
        Brightness[device] = brightness;
        return this;
    }

    public bool IsConnected(string device) => Connected.Contains(device);

    public uint? Read(string device) =>
        Connected.Contains(device) && !IgnoresDdc.Contains(device) && Brightness.TryGetValue(device, out var v) ? v : null;

    public bool Write(string device, uint value)
    {
        if (!Connected.Contains(device) || IgnoresDdc.Contains(device)) return false;
        Brightness[device] = value;
        Writes++;
        timeline.Events.Add($"write {device}={value}");
        return true;
    }
}

internal sealed class FakeStore(Timeline timeline) : IBrightnessStore
{
    public Dictionary<string, uint> Saved { get; private set; } = [];

    public bool FailSaves { get; set; }

    public Dictionary<string, uint> Load() => new(Saved);

    public bool Save(IReadOnlyDictionary<string, uint> originals)
    {
        if (FailSaves) return false;
        Saved = new Dictionary<string, uint>(originals);
        timeline.Events.Add($"save {string.Join(",", Saved.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))}");
        return true;
    }
}

internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("sidedim-tests-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
    }
}
