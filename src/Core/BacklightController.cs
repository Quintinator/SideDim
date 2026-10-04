namespace SideDim;

internal interface IBrightnessDevice
{
    bool IsConnected(string device);

    /// <summary>0-100, or null if the monitor doesn't answer.</summary>
    uint? Read(string device);

    bool Write(string device, uint value);
}

internal interface IBrightnessStore
{
    Dictionary<string, uint> Load();

    bool Save(IReadOnlyDictionary<string, uint> originals);
}

/// <remarks>Not thread-safe; <see cref="HardwareDimmer"/> runs it on a single worker thread.</remarks>
/// <param name="onNoDdc">Called on the caller's thread on every failed read, not just the first; only the log line is deduplicated.</param>
internal sealed class BacklightController(IBrightnessDevice device, IBrightnessStore store, Action<string>? onNoDdc = null)
{
    private readonly Dictionary<string, uint> _originals = store.Load();
    private readonly Dictionary<string, uint> _written = [];
    private readonly HashSet<string> _reportedSilent = [];

    public IReadOnlyDictionary<string, uint> Originals => _originals;

    public void Apply(IReadOnlyCollection<string> dim, uint level) => Apply(dim.ToDictionary(d => d, _ => level));

    /// <remarks>Never brightens a screen, never writes before the original brightness is saved, and retries a failed DDC/CI read on every call because a monitor that just woke up can miss one.</remarks>
    public void Apply(IReadOnlyDictionary<string, uint> levels)
    {
        foreach (var name in _originals.Keys.Where(n => !levels.ContainsKey(n)).ToList()) Restore(name);

        foreach (var (name, requested) in levels)
        {
            var level = Math.Min(requested, 100);
            if (!device.IsConnected(name)) continue;

            var firstTime = !_originals.TryGetValue(name, out var original);
            if (firstTime)
            {
                if (device.Read(name) is not { } current)
                {
                    if (_reportedSilent.Add(name))
                        Log.Write($"{name} does not answer DDC/CI, skipping it. Use Overlay, or turn on DDC/CI in the monitor's menu.");
                    onNoDdc?.Invoke(name);
                    continue;
                }
                _reportedSilent.Remove(name);
                original = current;
                _originals[name] = original;
                if (!store.Save(_originals))
                {
                    _originals.Remove(name);
                    Log.Write($"Not dimming {name}: could not save its brightness ({current}) for crash recovery.");
                    continue;
                }
            }

            var target = Math.Min(level, original);
            if (firstTime && target == original)
            {
                _written[name] = target;
                continue;
            }
            if (_written.TryGetValue(name, out var last) && last == target) continue;
            if (device.Write(name, target)) _written[name] = target;
            else Log.Write($"Could not dim {name}; will try again on the next change.");
        }
    }

    public void RestoreAll()
    {
        foreach (var name in _originals.Keys.ToList()) Restore(name);
    }

    /// <remarks>The first <c>_written.Remove</c> must stay so a later dim writes again if the restore fails; an unplugged monitor keeps its saved entry so it is restored when it comes back.</remarks>
    private void Restore(string name)
    {
        _written.Remove(name);
        if (!device.IsConnected(name)) return;
        if (!device.Write(name, _originals[name]))
        {
            Log.Write($"Could not restore brightness on {name}; will retry.");
            return;
        }
        _originals.Remove(name);
        _written.Remove(name);
        store.Save(_originals);
    }
}
