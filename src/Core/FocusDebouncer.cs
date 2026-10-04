namespace SideDim;

internal sealed class FocusDebouncer
{
    private string? _pending;
    private TimeSpan _pendingSince;

    /// <summary>The monitor kept bright; null when nothing is dimmed.</summary>
    public string? Applied { get; private set; }

    /// <param name="now">A monotonic timestamp, such as Stopwatch.Elapsed.</param>
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

    public void ForceApply(string? device)
    {
        Applied = device;
        _pending = device;
    }
}
