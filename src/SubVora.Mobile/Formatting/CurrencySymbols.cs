namespace SubVora.Mobile.Formatting;

/// <summary>
/// The symbol to print for an ISO-4217 code — ₹ for INR, £ for GBP, and so on.
/// <para>
/// Not <c>CultureInfo.NumberFormat.CurrencySymbol</c>, which resolves against the <em>device's</em>
/// culture rather than the money being shown. On a phone set to en-US that renders every amount
/// with a dollar sign, including a subscription billed in rupees. The code carried on each amount
/// is the only thing that knows what the money is, so the lookup keys on that.
/// </para>
/// <para>
/// Unknown codes return the code itself, which is why the app never shows a wrong symbol - the
/// worst case is the three letters it used to print everywhere.
/// </para>
/// </summary>
public static class CurrencySymbols
{
    /// <summary>
    /// Deliberately not exhaustive. A code earns a symbol only when that symbol identifies it on
    /// sight; anything ambiguous keeps its code, because "kr" against SEK, NOK and DKK, or ₨
    /// against PKR and LKR, tells the reader less than the letters do.
    /// <para>
    /// The dollar family is disambiguated the way CLDR does it - US$ is bare, everyone else carries
    /// a prefix - since a lone $ beside a converted total is exactly the ambiguity worth avoiding.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INR"] = "₹",
        ["USD"] = "$",
        ["EUR"] = "€",
        ["GBP"] = "£",
        ["JPY"] = "¥",
        ["CNY"] = "CN¥",
        ["KRW"] = "₩",
        ["RUB"] = "₽",
        ["TRY"] = "₺",
        ["THB"] = "฿",
        ["PHP"] = "₱",
        ["VND"] = "₫",
        ["ILS"] = "₪",
        ["NGN"] = "₦",
        ["BDT"] = "৳",
        ["UAH"] = "₴",
        ["KZT"] = "₸",
        ["BRL"] = "R$",
        ["AUD"] = "A$",
        ["CAD"] = "CA$",
        ["NZD"] = "NZ$",
        ["SGD"] = "S$",
        ["HKD"] = "HK$",
        ["MXN"] = "MX$",
    };

    /// <summary>The symbol for <paramref name="currencyCode"/>, or the code itself when there is no unambiguous one.</summary>
    public static string For(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return string.Empty;
        }

        var code = currencyCode.Trim();
        return ByCode.TryGetValue(code, out var symbol) ? symbol : code.ToUpperInvariant();
    }

    /// <summary>
    /// An amount with its symbol attached — "₹1,699.50", "CHF 42.00". No space after a true symbol
    /// and a space after a fallback code, so the code does not run into the digits.
    /// </summary>
    public static string Format(decimal amount, string? currencyCode, string numberFormat = "N2")
    {
        var symbol = For(currencyCode);
        var formattedAmount = amount.ToString(numberFormat);

        if (symbol.Length == 0)
        {
            return formattedAmount;
        }

        // A fallback is the ISO code itself: three ASCII letters. A real symbol never is.
        var isFallbackCode = symbol.Length == 3 && symbol.All(char.IsAsciiLetterUpper);
        return isFallbackCode ? $"{symbol} {formattedAmount}" : $"{symbol}{formattedAmount}";
    }
}
