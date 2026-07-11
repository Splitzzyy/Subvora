namespace SubVora.Application.Currency;

public interface IFxRateService
{
    /// <summary>Upserts each rate into fx_rates, keyed by (base_currency, target_currency).</summary>
    Task UpsertRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken cancellationToken = default);
}
