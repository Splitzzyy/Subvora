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
    public MatchConfidenceTier Tier { get; set; }
    public Guid? CatalogId { get; set; }
    public string? ProviderName { get; set; }
    public string? LogoUrl { get; set; }
    public Guid? CategoryId { get; set; }
}
