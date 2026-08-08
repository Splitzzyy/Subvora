using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Converters;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The name-to-icon mappings, tested apart from resolving them against the resource dictionary -
/// that step needs a running MAUI application, while the mapping is where the mistakes live.
/// </summary>
public class IconConverterTests
{
    [Theory]
    [InlineData("Entertainment", "IconMovie")]
    [InlineData("Food", "IconRestaurant")]
    [InlineData("Fitness", "IconFitness")]
    [InlineData("Utilities", "IconBolt")]
    [InlineData("Travel", "IconFlight")]
    [InlineData("Finance", "IconPayments")]
    [InlineData("Productivity", "IconWork")]
    public void CategoryIcon_ForASeededCategory_IsItsOwnIcon(string category, string expectedKey) =>
        Assert.Equal(expectedKey, CategoryIconConverter.IconKeyFor(category));

    [Fact]
    public void CategoryIcon_IsCaseInsensitive() =>
        // The dashboard groups by the name the API returns; nothing guarantees its casing survives
        // a round trip through the SQLite cache unchanged.
        Assert.Equal("IconMovie", CategoryIconConverter.IconKeyFor("entertainment"));

    [Theory]
    [InlineData("Pet insurance")]
    [InlineData("Uncategorized")]
    [InlineData("")]
    [InlineData(null)]
    public void CategoryIcon_ForAnythingUnrecognised_FallsBackRatherThanGuessing(string? category) =>
        // A guessed icon is worse than a generic one: a dinner plate against "Pet insurance" reads
        // as a bug, where the neutral marker reads as "no icon for this".
        Assert.Equal("IconCategory", CategoryIconConverter.IconKeyFor(category));

    [Fact]
    public void CategoryIconsAndColoursCoverTheSameCategories()
    {
        // A category with a hue but no icon - or the reverse - renders half-styled: a coloured tile
        // with a generic glyph, or the right glyph in neutral grey. Nothing else would catch that.
        var withIcons = CategoryIconConverter.KnownCategories
            .Where(category => !string.Equals(category, "Other", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var withColours = CategoryColorConverter.KnownCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(withColours.OrderBy(c => c), withIcons.OrderBy(c => c));
    }

    [Theory]
    [InlineData(PaymentSourceType.Card, "IconCreditCard")]
    [InlineData(PaymentSourceType.BankAccount, "IconBank")]
    [InlineData(PaymentSourceType.Wallet, "IconWallet")]
    [InlineData(PaymentSourceType.Other, "IconPayments")]
    public void PaymentSourceIcon_CoversEveryType(PaymentSourceType sourceType, string expectedKey) =>
        Assert.Equal(expectedKey, PaymentSourceIconConverter.IconKeyFor(sourceType));

    [Fact]
    public void PaymentSourceIcon_TheoryAboveCoversEveryMemberOfTheEnum()
    {
        // The theory pins one icon per member by name. This asserts the theory is still exhaustive:
        // add a member to PaymentSourceType and this fails, rather than the new type shipping with
        // the fallback wallet glyph and nobody noticing.
        //
        // Cannot be expressed as "no member maps to the fallback" - Wallet legitimately maps to
        // IconWallet, which is also the fallback key.
        Assert.Equal(4, Enum.GetValues<PaymentSourceType>().Length);
    }

    [Fact]
    public void PaymentSourceIcon_ForAValueOutsideTheEnum_FallsBack() =>
        Assert.Equal("IconWallet", PaymentSourceIconConverter.IconKeyFor((PaymentSourceType)(-1)));
}
