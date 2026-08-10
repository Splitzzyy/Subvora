namespace SubVora.Application.Currency;

public interface IFxRateService
{
    /// <summary>Upserts each rate into fx_rates, keyed by (base_currency, target_currency).</summary>
    Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads rates and their fetched_at from fx_rates for many base currencies against one target,
    /// in a single query, fetching any pair the cache missed so a brand-new currency is convertible
    /// before the next scheduled refresh. Returns only the pairs that resolved - a base currency
    /// absent from the result has no rate at all. Keyed case-insensitively. Age is returned rather
    /// than enforced: a stale rate still converts, and the caller reports how old it was.
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
