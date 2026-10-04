namespace SideDim;

/// <summary>
/// Turns the "which screen should stay bright" signal, sampled every poll, into stable decisions.
/// A new target has to hold for the dim delay (or the shorter restore delay) before it's applied,
/// so a quick alt-tab doesn't make every screen flicker.
/// </summary>
internal sealed class FocusDebouncer
{
    private string? _pending;
    private TimeSpan _pendingSince;

    /// <summary>The monitor currently kept bright; null when nothing is dimmed.</summary>
    public string? Applied { get; private set; }

    /// <param name="now">A monotonic timestamp, e.g. Stopwatch.Elapsed.</param>
    /// <returns>True when <see cref="Applied"/> changed and the caller should apply it.</returns>
    public bool Update(string? desired, TimeSpan now, TimeSpan dimDelay, TimeSpan restoreDelay)
    {
        if (desired != _pending || now < _pendingSince)
        {
            _pending = desired;
            _pendingSince = now;
        }

        if (desired == Applied) return false;

        var delay = desired is null ? restoreDelay : dimDelay;
        if (now - _pendingSince < delay) return false;

        Applied = desired;
        return true;
    }

    /// <summary>Apply right away, for the hotkey and the test button.</summary>
    public void ForceApply(string? device)
    {
        Applied = device;
        _pending = device;
    }
}
