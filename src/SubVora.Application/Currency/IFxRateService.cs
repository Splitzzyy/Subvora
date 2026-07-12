namespace SubVora.Application.Currency;

public interface IFxRateService
{
    /// <summary>Upserts each rate into fx_rates, keyed by (base_currency, target_currency).</summary>
    Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default);

    /// <summary>Reads a cached rate from fx_rates. Null if this pair hasn't been fetched yet - never calls a live API.</summary>
    Task<decimal?> GetRateAsync(string baseCurrency, string targetCurrency, CancellationToken cancellationToken = default);
}
