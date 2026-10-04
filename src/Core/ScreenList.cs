namespace SideDim;

internal sealed record ScreenInfo(Monitor Monitor, int Number, string Name, bool IsMain)
{
    public string Label => $"{Number}. {Name}{(IsMain ? " (main)" : "")}";
}

internal static class ScreenList
{
    public static List<ScreenInfo> Describe(IEnumerable<Monitor> monitors, IReadOnlyDictionary<string, string> names) => monitors
        .OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
        .Select((m, i) => new ScreenInfo(
            m,
            i + 1,
            names.TryGetValue(m.Id, out var name) ? name : "Screen",
            IsMain: m.Bounds.X == 0 && m.Bounds.Y == 0))
        .ToList();
}
