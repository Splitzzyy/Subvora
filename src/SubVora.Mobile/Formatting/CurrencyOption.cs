using System.Globalization;

namespace SubVora.Mobile.Formatting;

/// <summary>One entry in the home-currency picker.</summary>
/// <param name="Code">The ISO-4217 code, which is what gets stored and sent to the API.</param>
/// <param name="Name">The currency's English name, for people who do not think in codes.</param>
public sealed record CurrencyOption(string Code, string Name)
{
    /// <summary>
    /// What the picker row reads as - "₹  INR — Indian Rupee". The symbol leads because it is what
    /// someone recognises at a glance, the code follows because it is what the app stores, and the
    /// name resolves the ones no symbol identifies.
    /// </summary>
    public string Display => $"{CurrencySymbols.For(Code)}  {Code} — {Name}";

    public override string ToString() => Display;
}

/// <summary>
/// The currencies offered in Settings. Derived from the runtime's own region data rather than a
/// hand-written table - the same source <c>SubVora.Application.CurrencyCodes</c> validates against
/// on the server, so the picker cannot offer something the API would reject.
/// </summary>
public static class SupportedCurrencies
{
    /// <summary>Used when a profile has no currency yet, and the fallback the API applies too.</summary>
    public const string DefaultCode = "INR";

    private static readonly Lazy<IReadOnlyList<CurrencyOption>> Options = new(Build);

    public static IReadOnlyList<CurrencyOption> All => Options.Value;

    /// <summary>
    /// The list with <paramref name="code"/> guaranteed to be in it. A profile saved before a code
    /// left circulation - or by some other client - must still be selectable, or opening Settings
    /// would silently reset it to whatever the picker happened to land on.
    /// </summary>
    public static IReadOnlyList<CurrencyOption> Including(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || Find(code) is not null)
        {
            return All;
        }

        // Sorted position, not appended: a code hidden at the bottom of 150 entries is as good as
        // absent.
        var withUnknown = All.Concat([new CurrencyOption(code.Trim().ToUpperInvariant(), "Unrecognised currency")]);
        return [.. withUnknown.OrderBy(option => option.Code, StringComparer.Ordinal)];
    }

    public static CurrencyOption? Find(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(option => string.Equals(option.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<CurrencyOption> Build()
    {
        var byCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (!string.IsNullOrWhiteSpace(region.ISOCurrencySymbol))
                {
                    // Several cultures share a currency; first name in wins, and they agree.
                    byCode.TryAdd(region.ISOCurrencySymbol, region.CurrencyEnglishName);
                }
            }
            catch (ArgumentException)
            {
                // A handful of specific cultures do not resolve to a RegionInfo - skip them, same
                // as CurrencyCodes does on the server.
            }
        }

        // Ordinal by code: alphabetical, stable, and independent of the device's locale - a picker
        // that reorders itself because the phone language changed would be its own bug report.
        return [.. byCode
            .Select(entry => new CurrencyOption(entry.Key.ToUpperInvariant(), entry.Value))
            .OrderBy(option => option.Code, StringComparer.Ordinal)];
    }
}
