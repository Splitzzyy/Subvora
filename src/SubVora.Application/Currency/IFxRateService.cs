namespace SubVora.Application.Currency;

public interface IFxRateService
{
    /// <summary>Upserts each rate into fx_rates, keyed by (base_currency, target_currency).</summary>
    Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a rate and its fetched_at from fx_rates, fetching the pair once on a cache miss so a
    /// brand-new currency is convertible before the next scheduled refresh. Null when the provider
    /// has no such pair. Age is returned rather than enforced: a stale rate still converts, and the
    /// caller reports how old it was.
    /// </summary>
    Task<CachedFxRate?> GetRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);
}
