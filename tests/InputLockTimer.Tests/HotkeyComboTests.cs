using Xunit;

namespace InputLockTimer.Tests;

public class HotkeyComboTests
{
    [Theory]
    [InlineData("Ctrl+Alt+K", "Ctrl+Alt+K")]
    [InlineData("Shift+F12", "Shift+F12")]
    [InlineData("F12", "F12")]
    [InlineData(" ctrl + shift + 5 ", "Ctrl+Shift+5")]
    [InlineData("Alt+Ctrl+k", "Ctrl+Alt+K")]
    [InlineData("Ctrl+Enter", "Ctrl+Enter")]
    public void TryParse_AcceptsValidTextAndFormatsCanonically(string input, string expected)
    {
        Assert.True(HotkeyCombo.TryParse(input, out var combo));
        Assert.Equal(expected, combo.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Foo+K")]
    [InlineData("K+K")]
    [InlineData("Ctrl+Win+K")]
    [InlineData("Ctrl+Control, Shift")]
    [InlineData("Ctrl+123")]
    public void TryParse_RejectsInvalidText(string input)
    {
        Assert.False(HotkeyCombo.TryParse(input, out _));
    }

    [Fact]
    public void Validate_RejectsModifierOnlyKey()
    {
        Assert.True(HotkeyCombo.TryParse("Ctrl+ControlKey", out var combo));
        Assert.NotNull(combo.Validate());
    }

    [Fact]
    public void Validate_RejectsCtrlAltDelete()
    {
        Assert.True(HotkeyCombo.TryParse("Ctrl+Alt+Delete", out var combo));
        Assert.NotNull(combo.Validate());
    }

    [Theory]
    [InlineData("Ctrl+Alt+K")]
    [InlineData("Shift+F12")]
    [InlineData("F12")]
    public void Validate_AcceptsNormalCombos(string input)
    {
        Assert.True(HotkeyCombo.TryParse(input, out var combo));
        Assert.Null(combo.Validate());
    }

    [Theory]
    [InlineData("K")]
    [InlineData("Shift+K")]
    [InlineData("Ctrl+C")]
    [InlineData("Alt+F4")]
    public void GetWarnings_FlagsRiskyCombos(string input)
    {
        Assert.True(HotkeyCombo.TryParse(input, out var combo));
        Assert.NotEmpty(combo.GetWarnings());
    }

    [Theory]
    [InlineData("Ctrl+Alt+K")]
    [InlineData("Shift+F12")]
    public void GetWarnings_IsEmptyForDefaultStyleCombos(string input)
    {
        Assert.True(HotkeyCombo.TryParse(input, out var combo));
        Assert.Empty(combo.GetWarnings());
    }
}
