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

    /// <summary>
    /// <see cref="GetRateAsync"/> for many base currencies against one target, in a single query.
    /// Returns only the pairs that resolved - a base currency absent from the result has no rate,
    /// exactly as a null return means for the single-pair call. Keyed case-insensitively.
    /// <para>
    /// Exists because the burn-rate calculation asked per subscription, and a user with twenty USD
    /// subscriptions and an INR home currency issued the same query twenty times on the app's
    /// landing screen. The distinct pair count is bounded by how many currencies one person's
    /// subscriptions use - realistically two or three - so batching turns the whole thing into one
    /// round trip.
    /// </para>
    /// <para>
    /// A base currency equal to the target is dropped rather than looked up: nothing stores an
    /// identity rate, and the caller already treats that case as 1.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<string, CachedFxRate>> GetRatesAsync(
        IReadOnlyCollection<string> baseCurrencies,
        string targetCurrency,
        CancellationToken cancellationToken = default);
}
