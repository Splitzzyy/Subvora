using SQLite;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Models;

/// <summary>
/// sqlite-net-pcl mirror of SubscriptionDto, one row per subscription.
/// <para>
/// Must stay field-complete against <see cref="SubscriptionDto"/>. A partial mirror is a trap the
/// moment anything reads an edit back out of it: <c>Version</c> was missing, so a save built from a
/// cached row would carry 0, match no <c>xmin</c>, and 409 forever; <c>CatalogId</c> was missing, so
/// the same save would silently strip the record's catalog link - the exact loss
/// <c>SubscriptionDetailViewModel.ApplySubscription</c> orders its assignments to avoid.
/// <c>SqliteLocalCacheServiceTests</c> walks the DTO's properties by reflection so a field added
/// later fails the build rather than being quietly dropped.
/// </para>
/// </summary>
public class CachedSubscription
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string CustomName { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public BillingCycleType CycleCadence { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime NextBillingDate { get; set; }

    /// <summary>Nullable so "never marked paid" survives a round trip through the cache as itself.</summary>
    public DateTime? LastPaidDate { get; set; }

    public int AlertDaysAdvance { get; set; }

    /// <summary>
    /// Nullable, unlike the DTO's. sqlite-net adds a new column to an existing table on
    /// CreateTableAsync but leaves rows written before it existed at the default, and a cached
    /// version of 0 read as authoritative is worse than an absent one - it matches no <c>xmin</c>
    /// and turns every save built from that row into a permanent 409. Null means "this mirror does
    /// not know", which a caller can act on; 0 is a lie.
    /// </summary>
    public uint? Version { get; set; }

    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public string? PaymentSourceLabel { get; set; }
    public Guid? CatalogId { get; set; }
    public string? CatalogLogoUrl { get; set; }
    public bool IsFreeTrial { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static CachedSubscription FromDto(SubscriptionDto dto) => new()
    {
        Id = dto.Id,
        CustomName = dto.CustomName,
        CostAmount = dto.CostAmount,
        Currency = dto.Currency,
        CycleCadence = dto.CycleCadence,
        PurchaseDate = dto.PurchaseDate.ToDateTime(TimeOnly.MinValue),
        NextBillingDate = dto.NextBillingDate.ToDateTime(TimeOnly.MinValue),
        LastPaidDate = dto.LastPaidDate?.ToDateTime(TimeOnly.MinValue),
        AlertDaysAdvance = dto.AlertDaysAdvance,
        Version = dto.Version,
        CategoryId = dto.CategoryId,
        CategoryName = dto.CategoryName,
        PaymentSourceId = dto.PaymentSourceId,
        PaymentSourceLabel = dto.PaymentSourceLabel,
        CatalogId = dto.CatalogId,
        CatalogLogoUrl = dto.CatalogLogoUrl,
        IsFreeTrial = dto.IsFreeTrial,
        IsActive = dto.IsActive,
        CreatedAt = dto.CreatedAt,
    };

    public SubscriptionDto ToDto() => new()
    {
        Id = Id,
        CustomName = CustomName,
        CostAmount = CostAmount,
        Currency = Currency,
        CycleCadence = CycleCadence,
        PurchaseDate = DateOnly.FromDateTime(PurchaseDate),
        NextBillingDate = DateOnly.FromDateTime(NextBillingDate),
        LastPaidDate = LastPaidDate is null ? null : DateOnly.FromDateTime(LastPaidDate.Value),
        AlertDaysAdvance = AlertDaysAdvance,
        // 0 when the row predates the column. That is the same value the DTO would have had before
        // this field was mirrored at all, and SubscriptionDetailViewModel only ever sends a version
        // it read from the network, so nothing regresses - see the property's note above.
        Version = Version ?? 0,
        CategoryId = CategoryId,
        CategoryName = CategoryName,
        PaymentSourceId = PaymentSourceId,
        PaymentSourceLabel = PaymentSourceLabel,
        CatalogId = CatalogId,
        CatalogLogoUrl = CatalogLogoUrl,
        IsFreeTrial = IsFreeTrial,
        IsActive = IsActive,
        CreatedAt = CreatedAt,
    };
}
