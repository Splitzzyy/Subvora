namespace SubVora.Application.Matching;

public class ResolveSubscriptionResponse
{
    public MatchConfidenceTier Tier { get; set; }
    public Guid? CatalogId { get; set; }
    public string? ProviderName { get; set; }
    public string? LogoUrl { get; set; }
    public Guid? CategoryId { get; set; }
}
