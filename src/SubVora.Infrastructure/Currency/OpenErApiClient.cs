using System.Net.Http.Json;
using SubVora.Application.Currency;

namespace SubVora.Infrastructure.Currency;

/// <summary>
/// Wraps open.er-api.com's <c>/v6/latest/{base}</c> endpoint (ExchangeRate-API's open access tier):
/// no key, no account, rates refreshed daily. Its terms ask for attribution in the product.
/// <para>
/// Replaced exchangerate.host, which started requiring an access key and answered every keyless
/// call with HTTP 200 and <c>"success": false</c> - so no rate was ever stored and every
/// foreign-currency subscription sat in the dashboard's "no exchange rate available yet" list.
/// </para>
/// </summary>
public class OpenErApiClient : IExchangeRateClient
{
    private readonly HttpClient _httpClient;

    public OpenErApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ExchangeRate>> GetLatestRatesAsync(string baseCurrency, IReadOnlyCollection<string> targetCurrencies, CancellationToken cancellationToken = default)
    {
        if (targetCurrencies.Count == 0)
        {
            return [];
        }

        using var response = await _httpClient.GetAsync($"v6/latest/{Uri.EscapeDataString(baseCurrency)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenErApiResponse>(cancellationToken: cancellationToken);

        // The API reports failures (unknown base code, quota) in the body with a 200. Throwing lets
        // FxRateRefreshBackgroundService log it, instead of an empty list passing for "no rates".
        // StatusCode 200 marks it as "the provider answered": FxRateService treats that as a problem
        // with this one base, and anything else as the provider being down.
        if (payload is not { Result: "success" } || payload.Rates is null)
        {
            throw new HttpRequestException(
                $"Exchange rate lookup for {baseCurrency} failed: {payload?.ErrorType ?? "unreadable response"}.",
                inner: null,
                statusCode: System.Net.HttpStatusCode.OK);
        }

        var wanted = targetCurrencies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return payload.Rates
            .Where(pair => wanted.Contains(pair.Key))
            .Select(pair => new ExchangeRate(baseCurrency, pair.Key, pair.Value))
            .ToList();
    }

    private class OpenErApiResponse
    {
        public string? Result { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("error-type")]
        public string? ErrorType { get; set; }

        public Dictionary<string, decimal>? Rates { get; set; }
    }
}
