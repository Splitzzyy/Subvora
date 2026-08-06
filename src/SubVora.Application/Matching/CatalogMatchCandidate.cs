namespace SubVora.Application.Matching;

/// <summary>
/// The best-matching subscription_catalog row for a free-text query, plus its trigram similarity
/// score (0 = nothing in common, 1 = one string contains the other verbatim).
/// </summary>
public record CatalogMatchCandidate(Guid CatalogId, string ProviderName, Guid? CategoryId, string? LogoUrl, double Score);
