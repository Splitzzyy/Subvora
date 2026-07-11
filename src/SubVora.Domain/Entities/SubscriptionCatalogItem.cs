using Pgvector;

namespace SubVora.Domain.Entities;

public class SubscriptionCatalogItem
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? LogoUrl { get; set; }
    public Vector? SemanticEmbedding { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
