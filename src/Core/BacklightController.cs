namespace SideDim;

/// <summary>Monitor brightness access. The real one talks DDC/CI; tests use a fake.</summary>
internal interface IBrightnessDevice
{
    bool IsConnected(string device);

    /// <summary>Current brightness 0-100, or null if the monitor doesn't answer.</summary>
    uint? Read(string device);

    bool Write(string device, uint value);
}

/// <summary>Where the pre-dim brightness lives so it survives a crash.</summary>
internal interface IBrightnessStore
{
    Dictionary<string, uint> Load();

    void Save(IReadOnlyDictionary<string, uint> originals);
}

/// <summary>
/// Dims and restores monitor backlights. The brightness a monitor had before we touched it is saved
/// before the first write and only forgotten once it has been written back, so a crash, power cut or
/// unplugged monitor never leaves a screen stuck dark.
/// </summary>
/// <remarks>Not thread-safe; <see cref="HardwareDimmer"/> runs it on a single worker thread.</remarks>
internal sealed class BacklightController(IBrightnessDevice device, IBrightnessStore store)
{
    private readonly Dictionary<string, uint> _originals = store.Load();
    private readonly Dictionary<string, uint> _written = [];

    /// <summary>Monitors we have dimmed and their brightness before that.</summary>
    public IReadOnlyDictionary<string, uint> Originals => _originals;

    /// <summary>Dim exactly <paramref name="dim"/> to <paramref name="level"/>; restore every other monitor we dimmed.</summary>
    public void Apply(IReadOnlyCollection<string> dim, uint level)
    {
        level = Math.Min(level, 100);

        foreach (var name in _originals.Keys.Except(dim).ToList()) Restore(name);

        foreach (var name in dim)
        {
            if (!device.IsConnected(name)) continue;

            var firstTime = !_originals.TryGetValue(name, out var original);
            if (firstTime)
            {
                if (device.Read(name) is not { } current)
                {
                    Log.Write($"{name} does not answer DDC/CI, skipping it. Use Overlay, or turn on DDC/CI in the monitor's menu.");
                    continue;
                }
                original = current;
                _originals[name] = original;
                store.Save(_originals); // persist before the first write
            }

            // Never brighten a screen the user already turned down below the dim level.
            var target = Math.Min(level, original);
            if (firstTime && target == original)
            {
                _written[name] = target;
                continue;
            }
            if (_written.TryGetValue(name, out var last) && last == target) continue;
            if (device.Write(name, target)) _written[name] = target;
        }
    }

    /// <summary>Put back every brightness we changed, including leftovers from a previous run.</summary>
    public void RestoreAll()
    {
        foreach (var name in _originals.Keys.ToList()) Restore(name);
    }

    private void Restore(string name)
    {
        // An unplugged monitor keeps its entry, so it's restored when it comes back.
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
