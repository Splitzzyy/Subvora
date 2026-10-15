namespace SubVora.Application.Currency;

public interface IExchangeRateClient
{
    /// <summary>Gets current rates from <paramref name="baseCurrency"/> to each of <paramref name="targetCurrencies"/>.</summary>
    /// <exception cref="ExchangeRateLookupException">The provider answered without rates for this base.</exception>
    Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default);
}
