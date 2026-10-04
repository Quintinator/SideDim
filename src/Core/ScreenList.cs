namespace SideDim;

/// <param name="Number">1, 2, 3… from left to right, as shown by "Identify screens".</param>
/// <param name="Name">What the monitor calls itself, or "Screen" when it doesn't say.</param>
internal sealed record ScreenInfo(Monitor Monitor, int Number, string Name, bool IsMain)
{
    public string Label => $"{Number}. {Name}{(IsMain ? " (main)" : "")}";
}

internal static class ScreenList
{
    /// <summary>
    /// Numbers screens left to right (top to bottom when stacked), the way people point at them.
    /// Two identical monitors get the same name, so the number is what tells them apart.
    /// </summary>
    public static List<ScreenInfo> Describe(IEnumerable<Monitor> monitors, IReadOnlyDictionary<string, string> names) => monitors
        .OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
        .Select((m, i) => new ScreenInfo(
            m,
            i + 1,
            names.TryGetValue(m.Id, out var name) ? name : "Screen",
            IsMain: m.Bounds.X == 0 && m.Bounds.Y == 0))
        .ToList();
}
