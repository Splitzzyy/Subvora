using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubVora.Application.Auth;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Api.Tests;

/// <summary>The list and single reads carry each subscription's cost in the caller's home currency.</summary>
public class SubscriptionHomeCurrencyTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public SubscriptionHomeCurrencyTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"home-currency-{Guid.NewGuid()}@example.com";
        const string password = "Correct-horse-battery-staple1"; // pragma: allowlist secret

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task ListAndGet_ReturnHomeCurrencyFigures_CountingCyclesNotDays()
    {
        var client = await CreateAuthenticatedClientAsync();
        // INR is the registration default, so no live FX lookup is involved.
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            CustomName = "Notion",
            CostAmount = 400m,
            Currency = "INR",
            CycleCadence = BillingCycleType.Quarterly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 4, 1),
            AlertDaysAdvance = 3,
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var list = await client.GetFromJsonAsync<List<SubscriptionDto>>("/api/v1/subscriptions", JsonOptions);
        var row = Assert.Single(list!);
        Assert.Equal("INR", row.HomeCurrency);
        Assert.Equal(400m, row.HomeCurrencyAmount);
        Assert.Equal(1600m, row.HomeCurrencyYearly);
        Assert.Equal(133.33m, row.HomeCurrencyMonthly);
        // Display only: the stored figures are untouched.
        Assert.Equal(400m, row.CostAmount);
        Assert.Equal("INR", row.Currency);

        var single = await client.GetFromJsonAsync<SubscriptionDto>($"/api/v1/subscriptions/{created!.Id}", JsonOptions);
        Assert.Equal(1600m, single!.HomeCurrencyYearly);
    }
}
