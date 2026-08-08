using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace SubVora.Mobile.Converters;

/// <summary>
/// Maps a category name to its icon, the way <see cref="CategoryColorConverter"/> maps it to a
/// colour - same seeded set, same fallback rule, so a category's tile and its glyph always agree.
/// <para>
/// The geometry itself lives in Theme.xaml, not here: an icon defined in the resource dictionary is
/// visible to XAML and to this converter at once, so there is one copy of every path. This resolves
/// the key against the merged application resources and returns the geometry.
/// </para>
/// <para>
/// Anything unrecognised - and every user-created category - falls to a generic marker rather than
/// a guessed one. Inventing an icon from a name would be wrong more often than blank, and wrong is
/// worse: a plate against "Pet insurance" reads as a bug.
/// </para>
/// </summary>
public class CategoryIconConverter : IValueConverter
{
    private const string FallbackIconKey = "IconCategory";

    private static readonly Dictionary<string, string> IconKeyByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Entertainment"] = "IconMovie",
        ["Food"] = "IconRestaurant",
        ["Fitness"] = "IconFitness",
        ["Utilities"] = "IconBolt",
        ["Travel"] = "IconFlight",
        ["Finance"] = "IconPayments",
        ["Productivity"] = "IconWork",
        ["Other"] = FallbackIconKey,
    };

    /// <summary>The categories this converter has a specific icon for. Public so a test can hold it against <see cref="CategoryColorConverter.KnownCategories"/> - a category with a colour but no icon renders half-styled.</summary>
    public static IReadOnlyCollection<string> KnownCategories => IconKeyByCategory.Keys;

    /// <summary>
    /// The mapping on its own, separated from resolving it against the resource dictionary so it
    /// can be tested without a running MAUI application.
    /// </summary>
    public static string IconKeyFor(object? value) =>
        value is string name && IconKeyByCategory.TryGetValue(name, out var mapped)
            ? mapped
            : FallbackIconKey;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ResolveGeometry(IconKeyFor(value));

    /// <summary>
    /// Null rather than a throw when the key is missing. A binding failure here costs one blank
    /// 21px square; throwing from a converter inside a CollectionView item template takes the whole
    /// list down instead.
    /// </summary>
    internal static Geometry? ResolveGeometry(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var resource) == true
            ? resource as Geometry
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Category icons are display-only.");
}
