namespace SideDim.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", "Ctrl, Alt", Keys.D)]
    [InlineData("ctrl + alt + d", "Ctrl, Alt", Keys.D)]
    [InlineData("Control+Shift+F9", "Ctrl, Shift", Keys.F9)]
    [InlineData("Win+Alt+D1", "Alt, Win", Keys.D1)]
    [InlineData("F10", "None", Keys.F10)]
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
    public void Function_key_alone_is_allowed() =>
        Assert.Equal("F9", Hotkey.FromKeyPress(Keys.F9, ctrl: false, alt: false, shift: false)?.ToString());

    [Theory]
    [InlineData(Keys.ControlKey)]
    [InlineData(Keys.ShiftKey)]
    [InlineData(Keys.Menu)]
    [InlineData(Keys.LWin)]
    public void Modifier_on_its_own_is_not_a_hotkey_yet(Keys key) =>
        Assert.Null(Hotkey.FromKeyPress(key, ctrl: true, alt: false, shift: false));
}
