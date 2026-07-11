using System.Globalization;

namespace SubVora.Application.Subscriptions;

/// <summary>Known ISO-4217 currency codes, derived from the runtime's culture/region data.</summary>
public static class CurrencyCodes
{
    public static readonly IReadOnlySet<string> All = BuildCodes();

    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && All.Contains(code);

    private static HashSet<string> BuildCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                if (!string.IsNullOrWhiteSpace(region.ISOCurrencySymbol))
                {
                    codes.Add(region.ISOCurrencySymbol);
                }
            }
            catch (ArgumentException)
            {
                // A handful of specific cultures don't resolve to a valid RegionInfo - skip them.
            }
        }

        return codes;
    }
}
