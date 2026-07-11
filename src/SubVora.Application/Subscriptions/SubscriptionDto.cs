using SubVora.Domain.Enums;

namespace SubVora.Application.Subscriptions;

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public string CustomName { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public BillingCycleType CycleCadence { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public DateOnly NextBillingDate { get; set; }
    public int AlertDaysAdvance { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public string? PaymentSourceLabel { get; set; }
    public string? CatalogLogoUrl { get; set; }
    public bool IsFreeTrial { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
