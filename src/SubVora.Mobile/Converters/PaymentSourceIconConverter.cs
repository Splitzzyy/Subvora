using System.Globalization;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Converters;

/// <summary>
/// Maps a <see cref="PaymentSourceType"/> to its icon. The enum is a closed set of four, so unlike
/// categories every case is covered and the fallback only guards a value added to the enum later
/// without a matching icon.
/// </summary>
public class PaymentSourceIconConverter : IValueConverter
{
    private const string FallbackIconKey = "IconWallet";

    private static readonly Dictionary<PaymentSourceType, string> IconKeyByType = new()
    {
        [PaymentSourceType.Card] = "IconCreditCard",
        [PaymentSourceType.BankAccount] = "IconBank",
        [PaymentSourceType.Wallet] = "IconWallet",
        [PaymentSourceType.Other] = "IconPayments",
    };

    /// <summary>The mapping alone, testable without a running MAUI application.</summary>
    public static string IconKeyFor(object? value) =>
        value is PaymentSourceType sourceType && IconKeyByType.TryGetValue(sourceType, out var mapped)
            ? mapped
            : FallbackIconKey;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        CategoryIconConverter.ResolveGeometry(IconKeyFor(value));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Payment source icons are display-only.");
}

/// <summary>
/// The tint a payment source is drawn in. Deliberately one colour per <em>type</em> rather than per
/// source: a person's cards are not categories, and giving each row its own hue would imply a
/// distinction the data does not have.
/// </summary>
public class PaymentSourceColorConverter : IValueConverter
{
    private static readonly Dictionary<PaymentSourceType, (string Light, string Dark)> ColorByType = new()
    {
        [PaymentSourceType.Card] = ("#1F6FEB", "#5AA0FF"),
        [PaymentSourceType.BankAccount] = ("#0E8A80", "#3FD0C3"),
        [PaymentSourceType.Wallet] = ("#7A3FD0", "#A98CF0"),
        [PaymentSourceType.Other] = ("#8A8A99", "#9C9CAB"),
    };

    /// <summary>Matches CategoryColorConverter's soft wash, so tiles look the same weight across screens.</summary>
    private const float SoftAlpha = 0.20f;

    /// <summary>Same reasoning as CategoryColorConverter: 0.20 over a near-black surface reads muddy.</summary>
    private const float SoftAlphaDark = 0.32f;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        var pair = value is PaymentSourceType sourceType && ColorByType.TryGetValue(sourceType, out var found)
            ? found
            : ColorByType[PaymentSourceType.Other];

        var color = Color.FromArgb(isDark ? pair.Dark : pair.Light);

        return parameter as string == "soft" ? color.WithAlpha(isDark ? SoftAlphaDark : SoftAlpha) : color;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Payment source colours are display-only.");
}
