namespace SubVora.Mobile.Api.Dtos;

public enum BillingCycleType
{
    Weekly,
    Monthly,
    Yearly,
    OneTime,
}

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public string CustomName { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public BillingCycleType CycleCadence { get; set; }
    public DateOnly PurchaseDate { get; set; }
    public DateOnly NextBillingDate { get; set; }

    /// <summary>The billing date the user last marked paid, or null if they never have.</summary>
    public DateOnly? LastPaidDate { get; set; }

    public int AlertDaysAdvance { get; set; }

    /// <summary>
    /// True once the billing date has gone by without being marked paid. Nothing moves that date on
    /// a timer any more, so a date in the past means the charge is genuinely outstanding.
    /// Compared against the device's today, not the server's, so a user is never told a charge is
    /// late while it is still today where they are.
    /// </summary>
    public bool IsOverdue => IsActive && NextBillingDate < DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// The row's version when this was read. Sent back on update so the server can reject the save
    /// if the subscription changed in the meantime - see SubscriptionDetailViewModel.SaveAsync.
    /// </summary>
    public uint Version { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public string? PaymentSourceLabel { get; set; }
    public Guid? CatalogId { get; set; }
    public string? CatalogLogoUrl { get; set; }
    public bool IsFreeTrial { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateSubscriptionRequest
{
    public string CustomName { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public BillingCycleType CycleCadence { get; set; } = BillingCycleType.Monthly;
    public DateOnly PurchaseDate { get; set; }
    public DateOnly NextBillingDate { get; set; }
    /// <summary>Null means "use my global default" on create, and "leave it alone" on update.</summary>
    public int? AlertDaysAdvance { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? PaymentSourceId { get; set; }
    public Guid? CatalogId { get; set; }
    public bool IsFreeTrial { get; set; }

    /// <summary>Null preserves the stored state; false deactivates a cancelled subscription without deleting its history.</summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// The version of the record this edit was made against, on update. The server answers 409 if
    /// the subscription changed since. Null on create, where there is nothing to conflict with.
    /// </summary>
    public uint? Version { get; set; }
}

public enum MatchConfidenceTier
{
    AutoFill,
    SuggestConfirm,
    Manual,
}

public class ResolveSubscriptionRequest
{
    public string Input { get; set; } = string.Empty;
}

public class ResolveSubscriptionResponse
{
    /// <summary>How good the best candidate is. Wording only - the user picks from the list either way.</summary>
    public MatchConfidenceTier Tier { get; set; }

    /// <summary>Catalog rows that cleared the server's similarity floor, best first. Empty on Manual.</summary>
    public IReadOnlyList<CatalogMatchCandidate> Suggestions { get; set; } = [];
}

/// <summary>One row of the provider pick list. Mirrors the API's CatalogMatchCandidate.</summary>
public class CatalogMatchCandidate
{
    public Guid CatalogId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? LogoUrl { get; set; }
    public double Score { get; set; }
}
