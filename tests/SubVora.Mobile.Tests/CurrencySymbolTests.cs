using SubVora.Mobile.Formatting;

namespace SubVora.Mobile.Tests;

public class CurrencySymbolTests
{
    [Theory]
    [InlineData("INR", "₹")]
    [InlineData("USD", "$")]
    [InlineData("EUR", "€")]
    [InlineData("GBP", "£")]
    [InlineData("JPY", "¥")]
    public void For_AKnownCode_IsItsSymbol(string code, string expected) =>
        Assert.Equal(expected, CurrencySymbols.For(code));

    [Fact]
    public void For_IsCaseAndWhitespaceInsensitive() =>
        // The code arrives from the API, the SQLite cache and a free-text Entry on the detail
        // screen. Only the first of those is guaranteed to be trimmed and upper-cased.
        Assert.Equal("₹", CurrencySymbols.For(" inr "));

    [Theory]
    [InlineData("CHF")]
    [InlineData("SEK")]
    [InlineData("PKR")]
    public void For_ACodeWithNoUnambiguousSymbol_KeepsTheCode(string code) =>
        // "kr" means SEK, NOK and DKK; "₨" means PKR and LKR. Three letters say more than a symbol
        // shared with a different currency, so these deliberately stay as codes.
        Assert.Equal(code, CurrencySymbols.For(code));

    [Fact]
    public void For_AnUnrecognisedCode_ReturnsItUnchangedRatherThanGuessing() =>
        Assert.Equal("XYZ", CurrencySymbols.For("xyz"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_NothingUsable_IsEmptyNotAStraySymbol(string? code) =>
        Assert.Equal(string.Empty, CurrencySymbols.For(code));

    [Fact]
    public void TheDollarFamily_IsDisambiguated()
    {
        // A lone $ beside a converted total is the exact ambiguity worth avoiding - the dashboard
        // shows one number and nothing else says which dollar it is.
        Assert.Equal("$", CurrencySymbols.For("USD"));
        Assert.Equal("A$", CurrencySymbols.For("AUD"));
        Assert.Equal("CA$", CurrencySymbols.For("CAD"));
        Assert.Equal("S$", CurrencySymbols.For("SGD"));
    }

    [Fact]
    public void Format_PutsARealSymbolAgainstTheDigits() =>
        Assert.Equal("₹1,699.50", CurrencySymbols.Format(1699.50m, "INR"));

    [Fact]
    public void Format_SpacesAFallbackCodeSoItDoesNotRunIntoTheDigits() =>
        Assert.Equal("CHF 42.00", CurrencySymbols.Format(42m, "CHF"));

    [Fact]
    public void Format_WithNoCurrency_IsJustTheNumber() =>
        Assert.Equal("42.00", CurrencySymbols.Format(42m, null));

    [Fact]
    public void Format_HonoursTheNumberFormat() =>
        Assert.Equal("₹1,700", CurrencySymbols.Format(1699.50m, "INR", "N0"));
}
