using System.Net;
using System.Text;
using SubVora.Infrastructure.Currency;

namespace SubVora.Infrastructure.Tests;

/// <summary>No database - just the response parsing, against canned open.er-api.com bodies.</summary>
public class OpenErApiClientTests
{
    private static OpenErApiClient ClientReturning(string json, Action<Uri?>? onRequest = null) =>
        new(new HttpClient(new StubHandler(json, onRequest)) { BaseAddress = new Uri("https://open.er-api.com/") });

    [Fact]
    public async Task ReturnsOnlyTheRequestedTargets()
    {
        Uri? requested = null;
        var client = ClientReturning(
            """{"result":"success","base_code":"USD","rates":{"USD":1,"INR":88.12,"EUR":0.86}}""",
            uri => requested = uri);

        var rates = await client.GetLatestRatesAsync("USD", ["INR"]);

        Assert.Equal("https://open.er-api.com/v6/latest/USD", requested?.ToString());
        var rate = Assert.Single(rates);
        Assert.Equal(("USD", "INR", 88.12m), (rate.BaseCurrency, rate.TargetCurrency, rate.Rate));
    }

    [Fact]
    public async Task ThrowsWhenTheBodyReportsAnError()
    {
        // A 200 carrying a failure is exactly how the previous provider failed silently.
        var client = ClientReturning("""{"result":"error","error-type":"unsupported-code"}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestRatesAsync("XYZ", ["INR"]));
        Assert.Contains("unsupported-code", ex.Message);
        // How FxRateService tells "the provider answered, this base is the problem" from "the provider is down".
        Assert.Equal(System.Net.HttpStatusCode.OK, ex.StatusCode);
    }

    private sealed class StubHandler(string json, Action<Uri?>? onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest?.Invoke(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
