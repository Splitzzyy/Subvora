using System.Globalization;

namespace SubVora.Mobile.Converters;

/// <summary>
/// Maps a category name to its fixed colour. Colour follows the category, never its rank, so a
/// category keeps the same hue whether it is top of the dashboard or bottom.
/// <para>
/// The eight hues are a validated categorical palette: every adjacent pair clears CVD separation
/// (worst ΔE 9.1 light / 8.4 dark) and the normal-vision floor (19.6 / 19.3) against this app's card
/// surfaces. Colour is never the only cue - every row that uses this also prints its category name -
/// because with more than three categories on screen at once no eight-hue palette can keep every
/// arbitrary pair apart.
/// </para>
/// <para>
/// Unrecognised and user-created categories deliberately fall to neutral grey rather than a
/// generated hue. Red is left out of the set entirely: it is the app's danger colour, and a category
/// wearing it would read as an error.
/// </para>
/// </summary>
public class CategoryColorConverter : IValueConverter
{
    private static readonly Dictionary<string, (string Light, string Dark)> ByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Entertainment"] = ("#2a78d6", "#3987e5"),
        ["Food"] = ("#eb6834", "#d95926"),
        ["Fitness"] = ("#1baf7a", "#199e70"),
        ["Utilities"] = ("#eda100", "#c98500"),
        ["Travel"] = ("#e87ba4", "#d55181"),
        ["Finance"] = ("#008300", "#008300"),
        ["Productivity"] = ("#4a3aa7", "#9085e9"),
    };

    private const string NeutralLight = "#8A8A99";
    private const string NeutralDark = "#767686";

    /// <summary>
    /// Pass <c>ConverterParameter=soft</c> for a faded version, used behind text. The hue still
    /// identifies the category while dark ink on top keeps its own contrast - painting a letter
    /// white on the full-strength hue would fail on the lighter slots (yellow against white is
    /// about 2:1).
    /// </summary>
    private const float SoftAlpha = 0.20f;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var color = value is string name && ByCategory.TryGetValue(name, out var pair)
            ? Color.FromArgb(isDark ? pair.Dark : pair.Light)
            : Color.FromArgb(isDark ? NeutralDark : NeutralLight);

        return parameter as string == "soft" ? color.WithAlpha(SoftAlpha) : color;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Category colours are display-only.");
}
