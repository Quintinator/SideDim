namespace SideDim.Tests;

public class AltGrTests
{
    [Theory]
    [InlineData("Ctrl+Alt+E", true)]
    [InlineData("Ctrl+Alt+Shift+E", true)]
    [InlineData("Ctrl+Alt+F9", true)]
    [InlineData("Ctrl+Shift+E", false)]
    [InlineData("Alt+E", false)]
    [InlineData("Ctrl+E", false)]
    [InlineData("Ctrl+Alt+Win+E", false)]
    public void Only_Ctrl_Alt_without_Win_overlaps_AltGr(string text, bool overlaps)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(overlaps, AltGr.Overlaps(hotkey));
    }

    [Theory]
    [InlineData(1, "é", "é")]
    [InlineData(2, "ab", "ab")]
    [InlineData(-1, "´x", "´")]
    [InlineData(0, "é", null)]
    [InlineData(1, "\u0005", null)]
    [InlineData(9, "€", "€")]
    public void Reads_what_a_key_types(int count, string buffer, string? expected) =>
        Assert.Equal(expected, AltGr.Typed(count, buffer));

    [Fact]
    public void Describes_nothing_when_no_layout_types_anything() =>
        Assert.Null(AltGr.Describe([]));

    [Fact]
    public void Describes_one_or_two_layouts()
    {
        Assert.Equal("é on your United States-International keyboard",
            AltGr.Describe([("United States-International", "é"), ("United States-International", "é")]));
        Assert.Equal("é on your United States-International keyboard and € on your German keyboard",
            AltGr.Describe([("United States-International", "é"), ("German", "€")]));
    }

    [Theory]
    [InlineData(" ", "a no-break space on your French keyboard")]
    [InlineData(" ", "a narrow no-break space on your French keyboard")]
    [InlineData("­", "a soft hyphen on your French keyboard")]
    [InlineData("‏", "an invisible character on your French keyboard")]
    [InlineData("ָ", "◌ָ on your French keyboard")]
    [InlineData("&", "& on your French keyboard")]
    public void Names_characters_that_would_be_invisible(string typed, string expected) =>
        Assert.Equal(expected, AltGr.Describe([("French", typed)]));

    [Fact]
    public void A_plain_space_is_not_a_loss() =>
        Assert.Null(AltGr.Describe([("Romanian (Legacy)", " ")]));

    [Fact]
    public void Names_at_most_two_layouts()
    {
        Assert.Equal("a on your A keyboard, b on your B keyboard and on one more keyboard",
            AltGr.Describe([("A", "a"), ("B", "b"), ("C", "c")]));
        Assert.Equal("a on your A keyboard, b on your B keyboard and on 2 more keyboards",
            AltGr.Describe([("A", "a"), ("B", "b"), ("C", "c"), ("D", "d")]));
    }
}

public class KeyboardLayoutsTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F9")]
    [InlineData("Ctrl+Shift+E")]
    [InlineData("Ctrl+Alt+Win+E")]
    public void Function_keys_and_non_AltGr_combinations_type_nothing(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Empty(KeyboardLayouts.AltGrTyped(hotkey));
    }

    /// <remarks>An input language handle carries 0xF000 plus the layout's fixed "Layout Id" in its high word; United States-International is 0001.</remarks>
    [Fact]
    public void Finds_what_AltGr_types_on_an_installed_US_International_layout()
    {
        var installed = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().Any(l => ((long)l.Handle >> 16 & 0xFFFF) == 0xF001);
        Assert.SkipUnless(installed, "United States-International is not installed on this PC");

        Assert.True(Hotkey.TryParse("Ctrl+Alt+E", out var e));
        Assert.Contains(KeyboardLayouts.AltGrTyped(e), t => t.Typed == "é");
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Shift+E", out var shiftE));
        Assert.Contains(KeyboardLayouts.AltGrTyped(shiftE), t => t.Typed == "É");
    }
}
