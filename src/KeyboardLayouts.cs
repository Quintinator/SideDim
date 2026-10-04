using System.Globalization;

namespace SideDim;

internal static class KeyboardLayouts
{
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LSHIFT = 0xA0, VK_LCONTROL = 0xA2, VK_RMENU = 0xA5;

    /// <summary>What the hotkey's AltGr combination types on each installed keyboard layout that types anything with it.</summary>
    /// <remarks>ToUnicodeEx is told not to change the keyboard state, so probing can't swallow a dead key the user is typing.</remarks>
    public static IReadOnlyList<(string Layout, string Typed)> AltGrTyped(Hotkey hotkey)
    {
        if (!AltGr.Overlaps(hotkey)) return [];

        var state = new byte[256];
        state[VK_CONTROL] = state[VK_LCONTROL] = state[VK_MENU] = state[VK_RMENU] = 0x80;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) state[VK_SHIFT] = state[VK_LSHIFT] = 0x80;

        var found = new List<(string, string)>();
        var buffer = new char[8];
        foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
        {
            var scanCode = Native.MapVirtualKeyEx((uint)hotkey.Key, Native.MAPVK_VK_TO_VSC, language.Handle);
            var count = Native.ToUnicodeEx((uint)hotkey.Key, scanCode, state, buffer, buffer.Length, Native.TOUNICODE_KEEP_STATE, language.Handle);
            if (AltGr.Typed(count, buffer) is { } typed) found.Add((Name(language), typed));
        }
        return found;
    }

    /// <remarks>WinForms calls a layout it can't find in the registry "Unknown layout", and Culture throws for a language .NET doesn't know.</remarks>
    private static string Name(InputLanguage language)
    {
        var name = language.LayoutName;
        if (!string.IsNullOrWhiteSpace(name) && name != "Unknown layout") return name;
        try
        {
            return language.Culture.DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return name;
        }
    }
}
