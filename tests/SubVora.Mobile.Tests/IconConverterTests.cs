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
        //
        // "Other" is excluded deliberately and is the one permitted asymmetry: it has its own glyph
        // (so it is not confused with an unrecognised category - see below) but takes the neutral
        // grey, because the palette's eight hues are a validated set and inventing a ninth for it
        // would not be.
        var withIcons = CategoryIconConverter.KnownCategories
            .Where(category => !string.Equals(category, "Other", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var withColours = CategoryColorConverter.KnownCategories.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(withColours.OrderBy(c => c), withIcons.OrderBy(c => c));
    }

    [Fact]
    public void SeededCategories_NeverShareTheUnrecognisedFallbackGlyph()
    {
        // The reported bug: "Other" was mapped to the fallback key, so the system category "Other"
        // and a user's "Music" drew the same glyph - and, both missing a hue, the same neutral grey.
        // The two tiles were pixel-identical. The fallback has to mean "no icon for this" and
        // nothing else.
        var fallback = CategoryIconConverter.IconKeyFor("Pet insurance");

        Assert.All(
            CategoryIconConverter.KnownCategories,
            category => Assert.NotEqual(fallback, CategoryIconConverter.IconKeyFor(category)));
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
