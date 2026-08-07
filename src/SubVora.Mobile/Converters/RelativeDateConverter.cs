using System.Globalization;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Converters;

/// <summary>Binds <see cref="RelativeDate.Describe(DateOnly)"/> straight onto a row's billing date.</summary>
public class RelativeDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly date ? RelativeDate.Describe(date) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Relative dates are display-only.");
}
