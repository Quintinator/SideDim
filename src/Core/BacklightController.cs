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

    /// <summary>False when the backup couldn't be written.</summary>
    bool Save(IReadOnlyDictionary<string, uint> originals);
}

/// <summary>
/// Dims and restores monitor backlights. The brightness a monitor had before we touched it is saved
/// before the first write and only forgotten once it has been written back, so a crash, power cut or
/// unplugged monitor never leaves a screen stuck dark.
/// </summary>
/// <remarks>Not thread-safe; <see cref="HardwareDimmer"/> runs it on a single worker thread.</remarks>
/// <param name="onNoDdc">
/// Called (on the caller's thread) whenever a monitor doesn't answer DDC/CI when it's about to be dimmed.
/// The log line is written once; whether and when to tell the user is the caller's call.
/// </param>
internal sealed class BacklightController(IBrightnessDevice device, IBrightnessStore store, Action<string>? onNoDdc = null)
{
    private readonly Dictionary<string, uint> _originals = store.Load();
    private readonly Dictionary<string, uint> _written = [];
    private readonly HashSet<string> _reportedSilent = []; // monitors already logged as not answering DDC/CI

    /// <summary>Monitors we have dimmed and their brightness before that.</summary>
    public IReadOnlyDictionary<string, uint> Originals => _originals;

    /// <summary>Dim exactly <paramref name="dim"/> to one <paramref name="level"/>; restore every other monitor we dimmed.</summary>
    public void Apply(IReadOnlyCollection<string> dim, uint level) => Apply(dim.ToDictionary(d => d, _ => level));

    /// <summary>Dim exactly these monitors, each to its own level; restore every other monitor we dimmed.</summary>
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
                // Keep reading every time (a monitor that just woke up can miss one), but only report once.
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
                    // Without the backup a crash could leave this screen dark for good, so don't touch it.
                    _originals.Remove(name);
                    Log.Write($"Not dimming {name}: could not save its brightness ({current}) for crash recovery.");
                    continue;
                }
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
            else Log.Write($"Could not dim {name}; will try again on the next change.");
        }
    }

    /// <summary>Put back every brightness we changed, including leftovers from a previous run.</summary>
    public void RestoreAll()
    {
        foreach (var name in _originals.Keys.ToList()) Restore(name);
    }

    private void Restore(string name)
    {
        _written.Remove(name); // whatever happens next, a later dim must write again
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
