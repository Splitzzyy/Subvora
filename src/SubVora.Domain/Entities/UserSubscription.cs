using SubVora.Domain.Enums;

namespace SubVora.Domain.Entities;

public class UserSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CatalogId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public string CustomName { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public BillingCycleType CycleCadence { get; set; } = BillingCycleType.Monthly;
    public DateOnly PurchaseDate { get; set; }
    public DateOnly NextBillingDate { get; set; }

    /// <summary>
    /// The billing date the user last confirmed they paid, or null if they never have. Set by
    /// marking a subscription paid, which also moves <see cref="NextBillingDate"/> on one cycle.
    /// Nothing advances the billing date on its own, so a date left in the past means the charge is
    /// genuinely outstanding rather than merely stale.
    /// </summary>
    public DateOnly? LastPaidDate { get; set; }
    public int AlertDaysAdvance { get; set; } = 3;
    public bool IsFreeTrial { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
