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
    public bool IsFreeTrial { get; set; }
}
