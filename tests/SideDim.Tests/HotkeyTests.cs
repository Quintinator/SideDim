namespace SideDim.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", "Ctrl, Alt", Keys.D)]
    [InlineData("ctrl + alt + d", "Ctrl, Alt", Keys.D)]
    [InlineData("Control+Shift+F9", "Ctrl, Shift", Keys.F9)]
    [InlineData("Win+Alt+D1", "Alt, Win", Keys.D1)]
    [InlineData("Shift+Ctrl+F5", "Ctrl, Shift", Keys.F5)]
    [InlineData("Ctrl+Alt+F4", "Ctrl, Alt", Keys.F4)]
    public void Parses_common_spellings(string text, string mods, Keys key)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(Enum.Parse<HotkeyModifiers>(mods), hk.Modifiers);
        Assert.Equal(key, hk.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+D+E")]
    [InlineData("Ctrl+ControlKey")]
    [InlineData("Ctrl+42")]
    [InlineData("Ctrl+A,B")]
    [InlineData("F10")]
    [InlineData("A")]
    [InlineData("Shift+A")]
    [InlineData("Alt+F4")]
    public void Rejects_nonsense(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Round_trips_through_text()
    {
        Assert.True(Hotkey.TryParse("shift+ctrl+alt+k", out var hk));
        Assert.Equal("Ctrl+Alt+Shift+K", hk.ToString());
        Assert.True(Hotkey.TryParse(hk.ToString(), out var again));
        Assert.Equal(hk, again);
    }

    [Fact]
    public void Key_press_with_modifiers_becomes_a_hotkey() =>
        Assert.Equal("Ctrl+Alt+G", Hotkey.FromKeyPress(Keys.G, ctrl: true, alt: true, shift: false)?.ToString());

    [Fact]
    public void Plain_letter_is_refused_because_it_would_eat_normal_typing() =>
        Assert.Null(Hotkey.FromKeyPress(Keys.G, ctrl: false, alt: false, shift: false));

    [Fact]
    public void Shift_plus_letter_is_refused_for_the_same_reason() =>
        Assert.Null(Hotkey.FromKeyPress(Keys.G, ctrl: false, alt: false, shift: true));

    [Fact]
    public void Function_key_alone_is_refused_because_games_use_them() =>
        Assert.Null(Hotkey.FromKeyPress(Keys.F5, ctrl: false, alt: false, shift: false));

    [Fact]
    public void Alt_F4_is_refused_because_it_would_stop_windows_closing()
    {
        Assert.Null(Hotkey.FromKeyPress(Keys.F4, ctrl: false, alt: true, shift: false));
        Assert.Equal("Ctrl+Alt+F4", Hotkey.FromKeyPress(Keys.F4, ctrl: true, alt: true, shift: false)?.ToString());
    }

    [Fact]
    public void Default_hotkey_avoids_AltGr_letters_and_parses()
    {
        Assert.True(Hotkey.TryParse(Settings.DefaultHotkey, out var hk));
        Assert.InRange(hk.Key, Keys.F1, Keys.F24); // Ctrl+Alt+letter would be AltGr+letter on European layouts
    }

    [Theory]
    [InlineData(Keys.ControlKey)]
    [InlineData(Keys.ShiftKey)]
    [InlineData(Keys.Menu)]
    [InlineData(Keys.LWin)]
    public void Modifier_on_its_own_is_not_a_hotkey_yet(Keys key) =>
        Assert.Null(Hotkey.FromKeyPress(key, ctrl: true, alt: false, shift: false));
}
