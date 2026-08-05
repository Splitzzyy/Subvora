using SubVora.Domain.Enums;

namespace SubVora.Application.Subscriptions;

public class CreateSubscriptionRequest
{
    public string CustomName { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public BillingCycleType CycleCadence { get; set; } = BillingCycleType.Monthly;
    public DateOnly PurchaseDate { get; set; }
    public DateOnly NextBillingDate { get; set; }
    public int AlertDaysAdvance { get; set; } = 3;
    public Guid? CategoryId { get; set; }
    public Guid? PaymentSourceId { get; set; }

    /// <summary>Optional subscription_catalog reference, as returned by POST /api/v1/subscriptions/resolve. Null detaches any existing link on update.</summary>
    public Guid? CatalogId { get; set; }
    public bool IsFreeTrial { get; set; }
}
