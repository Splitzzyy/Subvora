namespace SubVora.Infrastructure.Catalog;

/// <summary>
/// One row of subscription-catalog.json. There is deliberately no id here: provider_name already
/// carries a unique index, so it is the natural key and nothing has to be hand-assigned to add a
/// brand.
/// </summary>
/// <param name="ProviderName">Canonical brand name, matched against free-text input by pg_trgm.</param>
/// <param name="Category">Name of a system category (user_id IS NULL). An unknown name is skipped.</param>
/// <param name="IconSlug">
/// Simple Icons slug, or null when the brand has no icon in the set - v13 dropped several for
/// trademark reasons. A null slug stores a null logo_url, which the mobile list already renders
/// with its placeholder; matching does not need a logo.
/// </param>
public sealed record SubscriptionCatalogProvider(string ProviderName, string Category, string? IconSlug)
{
    /// <summary>CC0 icon set on the jsDelivr CDN, pinned to major v13. The brand marks themselves remain their owners' and are used nominatively.</summary>
    public string? LogoUrl => IconSlug is null
        ? null
        : $"https://cdn.jsdelivr.net/npm/simple-icons@13/icons/{IconSlug}.svg";
}
