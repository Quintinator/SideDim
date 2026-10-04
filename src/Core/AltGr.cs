using System.Globalization;

namespace SideDim;

/// <summary>
/// Windows sends Ctrl+Alt for the AltGr key, so a Ctrl+Alt hotkey also catches AltGr and stops it typing characters
/// such as é or € on keyboard layouts that have AltGr.
/// </summary>
internal static class AltGr
{
    public static bool Overlaps(Hotkey hotkey) =>
        (hotkey.Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt);

    /// <summary>Reads a ToUnicodeEx result. A negative count is a dead key, whose accent is still in the buffer.</summary>
    /// <returns>The typed text, or null when the key types nothing or only control characters.</returns>
    public static string? Typed(int count, ReadOnlySpan<char> buffer)
    {
        var text = count < 0 ? buffer[..Math.Min(1, buffer.Length)] : buffer[..Math.Min(count, buffer.Length)];
        if (text.IsEmpty) return null;
        foreach (var c in text)
            if (char.IsControl(c)) return null;
        return new string(text);
    }

    /// <summary>For example "é on your United States-International keyboard". Names at most two layouts, so it fits in a notification.</summary>
    /// <returns>Null when no layout types anything with this combination.</returns>
    public static string? Describe(IReadOnlyList<(string Layout, string Typed)> typed)
    {
        var parts = typed.Distinct()
            .Select(t => Shown(t.Typed) is { } shown ? $"{shown} on your {t.Layout} keyboard" : null)
            .OfType<string>()
            .ToList();
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            3 => $"{parts[0]}, {parts[1]} and on one more keyboard",
            _ => $"{parts[0]}, {parts[1]} and on {parts.Count - 2} more keyboards",
        };
    }

    /// <remarks>
    /// Some layouts put a no-break space, soft hyphen, direction mark or lone accent on AltGr. Printed as they are, these
    /// would be invisible or sit on the word before, so they are named or shown on a dotted circle instead.
    /// A plain space is left out because the Space key alone still types it.
    /// </remarks>
    private static string? Shown(string typed) => typed switch
    {
        " " => null,
        "\u00A0" => "a no-break space",
        "\u202F" => "a narrow no-break space",
        "\u00AD" => "a soft hyphen",
        _ when typed.All(c => char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format) => "an invisible character",
        _ when char.GetUnicodeCategory(typed[0]) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => "\u25CC" + typed,
        _ => typed,
    };
}
