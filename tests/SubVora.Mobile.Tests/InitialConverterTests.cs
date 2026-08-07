using System.Globalization;
using SubVora.Mobile.Converters;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The provider tile is generated from the subscription's name, so it has to cope with whatever the
/// user typed - including nothing.
/// </summary>
public class InitialConverterTests
{
    private static string Convert(string? name) =>
        (string)new InitialConverter().Convert(name, typeof(string), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("Netflix", "N")]
    [InlineData("spotify", "S")]
    [InlineData("  Amazon Prime Video", "A")]
    [InlineData("1Password", "1")]
    public void UsesTheFirstVisibleCharacterUppercased(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FallsBackWhenThereIsNothingToShow(string? name)
    {
        Assert.Equal("?", Convert(name));
    }

    [Fact]
    public void KeepsANonBmpFirstCharacterWhole()
    {
        // Taking char[0] would hand back half a surrogate pair, which renders as a broken box.
        Assert.Equal("\U0001F3AC", Convert("\U0001F3AC Movie Club"));
    }
}
