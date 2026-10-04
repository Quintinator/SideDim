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

    /// <summary>Parses text like "Ctrl+Alt+D". Exactly one non-modifier key is required.</summary>
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

        if (key is null) return false;
        hotkey = new Hotkey(mods, key.Value);
        return true;
    }

    /// <summary>
    /// Turns a key press in the hotkey box into a hotkey, or null if it isn't one (yet).
    /// A letter without Ctrl or Alt is refused, because it would swallow that key in every app.
    /// </summary>
    public static Hotkey? FromKeyPress(Keys key, bool ctrl, bool alt, bool shift)
    {
        if (ModifierKeys.Contains(key) || key == Keys.None) return null;
        var isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
        if (!ctrl && !alt && !isFunctionKey) return null;

        var mods = HotkeyModifiers.None;
        if (ctrl) mods |= HotkeyModifiers.Ctrl;
        if (alt) mods |= HotkeyModifiers.Alt;
        if (shift) mods |= HotkeyModifiers.Shift;
        return new Hotkey(mods, key);
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
        // Enum.TryParse also accepts numbers ("42") and flag combinations; only real key names count.
        key = Keys.None;
        if (text.All(char.IsDigit)) return false;
        if (!Enum.TryParse(text, ignoreCase: true, out key)) return false;
        return key != Keys.None && (key & Keys.Modifiers) == 0 && Enum.IsDefined(key) && !ModifierKeys.Contains(key);
    }
}
