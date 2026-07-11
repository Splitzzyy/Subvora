using System.Net.Http.Json;
using SubVora.Application.Currency;

namespace SubVora.Infrastructure.Currency;

/// <summary>Wraps exchangerate.host's /latest endpoint. Free tier, no API key required.</summary>
public class ExchangeRateHostClient : IExchangeRateClient
{
    private readonly HttpClient _httpClient;

    public ExchangeRateHostClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default)
    {
        if (targetCurrencies.Count == 0)
        {
            return [];
        }

        var symbols = string.Join(',', targetCurrencies);
        var requestUri = $"latest?base={Uri.EscapeDataString(baseCurrency)}&symbols={Uri.EscapeDataString(symbols)}";

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ExchangeRateHostResponse>(cancellationToken: cancellationToken);
        if (payload?.Rates is null)
        {
            return [];
        }

        return payload.Rates
            .Select(pair => new ExchangeRate(baseCurrency, pair.Key, pair.Value))
            .ToList();
    }

    private class ExchangeRateHostResponse
    {
        public Dictionary<string, decimal> Rates { get; set; } = new();
    }
}
