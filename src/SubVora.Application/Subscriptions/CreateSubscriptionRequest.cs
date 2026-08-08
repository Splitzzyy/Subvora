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
    /// <summary>Reminder lead time. Omitted on create means "use my global default" (users.default_alert_days_advance, then 3); omitted on update leaves the stored value alone.</summary>
    public int? AlertDaysAdvance { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? PaymentSourceId { get; set; }

    /// <summary>Optional subscription_catalog reference, as returned by POST /api/v1/subscriptions/resolve. Null detaches any existing link on update.</summary>
    public Guid? CatalogId { get; set; }
    public bool IsFreeTrial { get; set; }

    /// <summary>Deactivates or reactivates a subscription on update. Nullable so an update that omits it preserves the current state; create always produces an active subscription.</summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// The <see cref="SubscriptionDto.Version"/> this edit was made against, on update. When
    /// present the server refuses the write if the row has changed since - the case that matters is
    /// an edit opened before a mark-paid, which would otherwise write the pre-payment billing date
    /// back and silently reverse the payment.
    /// <para>
    /// Optional, and ignored on create. Null keeps the old last-write-wins behaviour, so an already
    /// installed APK that does not know about this field keeps working.
    /// </para>
    /// </summary>
    public uint? Version { get; set; }
}
