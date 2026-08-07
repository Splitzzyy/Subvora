using System.Globalization;
using System.Text;

namespace SubVora.Mobile.Converters;

/// <summary>
/// First letter of a subscription's name, for the tile that stands in for a provider logo.
/// <para>
/// The catalog stores a Simple Icons URL per provider, but those are SVGs and MAUI hands remote
/// images to Android's bitmap decoder, which cannot read SVG - so brand logos never drew, and the
/// placeholder underneath showed through. A letter is generated on the device instead: it needs no
/// network (this list is available offline from the SQLite mirror, where a remote logo would be
/// blank anyway), and every subscription gets one whether or not it matched the catalog.
/// </para>
/// </summary>
public class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = (value as string)?.TrimStart();
        if (string.IsNullOrEmpty(name))
        {
            return "?";
        }

        // Rune, not char: an emoji or non-BMP first character would otherwise be split into half a
        // surrogate pair and render as a replacement box.
        var first = name.EnumerateRunes().First();
        return Rune.ToUpper(first, culture).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Initials are display-only.");
}
