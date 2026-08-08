using System.Globalization;
using SubVora.Mobile.Formatting;

namespace SubVora.Mobile.Converters;

/// <summary>
/// XAML wrapper over <see cref="CurrencySymbols.For"/>: binds an ISO-4217 code and renders the
/// symbol. Every amount in the app carries its own code - a subscription billed in dollars sits in
/// the same list as one billed in rupees - so the symbol is resolved per value rather than once
/// from the device's culture.
/// </summary>
public class CurrencySymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        CurrencySymbols.For(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Currency symbols are display-only; the stored value is always the ISO code.");
}
