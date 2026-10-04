namespace SideDim;

/// <summary>Values match the MOD_* flags RegisterHotKey expects.</summary>
[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

internal readonly record struct Hotkey(HotkeyModifiers Modifiers, Keys Key)
{
    private static readonly HashSet<Keys> ModifierKeys =
    [
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin,
    ];

    /// <summary>
    /// Parses text like "Ctrl+Alt+F9". Exactly one non-modifier key is required, plus Ctrl, Alt or Win:
    /// a global hotkey swallows its keys in every app, so bare keys and Shift-only combos are refused.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = HotkeyModifiers.None;
        Keys? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win": mods |= HotkeyModifiers.Win; continue;
            }

            if (key is not null || !IsKeyName(part, out var parsed)) return false;
            key = parsed;
        }

        if (key is null || !IsSafe(mods, key.Value)) return false;
        hotkey = new Hotkey(mods, key.Value);
        return true;
    }

    private static bool IsSafe(HotkeyModifiers mods, Keys key)
    {
        if ((mods & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0) return false;
        return !(mods == HotkeyModifiers.Alt && key == Keys.F4); // would stop every window from closing
    }

    /// <summary>
    /// Turns a key press in the hotkey box into a hotkey, or null if it isn't one (yet).
    /// A global hotkey swallows its keys in every app, so it needs Ctrl or Alt: a bare F5 would steal
    /// quicksave from games, and Alt+F4 would stop windows from closing.
    /// </summary>
    public static Hotkey? FromKeyPress(Keys key, bool ctrl, bool alt, bool shift)
    {
        if (ModifierKeys.Contains(key) || key == Keys.None) return null;

        var mods = HotkeyModifiers.None;
        if (ctrl) mods |= HotkeyModifiers.Ctrl;
        if (alt) mods |= HotkeyModifiers.Alt;
        if (shift) mods |= HotkeyModifiers.Shift;
        return IsSafe(mods, key) ? new Hotkey(mods, key) : null;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    private static bool IsKeyName(string text, out Keys key)
    {
        // Enum.TryParse also accepts numbers ("42") and comma lists of flags ("A,B"); only real key names count.
        key = Keys.None;
        if (text.All(char.IsDigit) || text.Contains(',')) return false;
        if (!Enum.TryParse(text, ignoreCase: true, out key)) return false;
        return key != Keys.None && (key & Keys.Modifiers) == 0 && Enum.IsDefined(key) && !ModifierKeys.Contains(key);
    }
}
