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

    /// <summary>
    /// The billing date the user last marked paid, or null if they never have. Clients decide
    /// "overdue" by comparing <see cref="NextBillingDate"/> against the device's today rather than
    /// the server's, so a user in a different timezone is not told a charge is late an hour early.
    /// </summary>
    public DateOnly? LastPaidDate { get; set; }

    public int AlertDaysAdvance { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public string? PaymentSourceLabel { get; set; }

    /// <summary>Returned so a client editing this subscription can round-trip the link back on save instead of silently stripping it.</summary>
    public Guid? CatalogId { get; set; }
    public string? CatalogLogoUrl { get; set; }
    public bool IsFreeTrial { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The row's version when it was read, from Postgres' xmin. Send it back on update and the
    /// server rejects the write with 409 if anything changed in between, instead of silently
    /// overwriting it. Clients that ignore this field keep the previous last-write-wins behaviour.
    /// </summary>
    public uint Version { get; set; }
}
