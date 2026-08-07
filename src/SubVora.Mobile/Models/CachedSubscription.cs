using SQLite;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Models;

/// <summary>sqlite-net-pcl mirror of SubscriptionDto, one row per subscription.</summary>
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
    public int AlertDaysAdvance { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public string? PaymentSourceLabel { get; set; }
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
        AlertDaysAdvance = dto.AlertDaysAdvance,
        CategoryId = dto.CategoryId,
        CategoryName = dto.CategoryName,
        PaymentSourceId = dto.PaymentSourceId,
        PaymentSourceLabel = dto.PaymentSourceLabel,
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
        AlertDaysAdvance = AlertDaysAdvance,
        CategoryId = CategoryId,
        CategoryName = CategoryName,
        PaymentSourceId = PaymentSourceId,
        PaymentSourceLabel = PaymentSourceLabel,
        CatalogLogoUrl = CatalogLogoUrl,
        IsFreeTrial = IsFreeTrial,
        IsActive = IsActive,
        CreatedAt = CreatedAt,
    };
}
