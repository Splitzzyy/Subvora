namespace SubVora.Application.Matching;

/// <summary>The nearest subscription_catalog row to a query embedding, plus its pgvector cosine distance (0 = identical, 2 = opposite).</summary>
public record CatalogMatchCandidate(Guid CatalogId, string ProviderName, Guid? CategoryId, string? LogoUrl, double Distance);
