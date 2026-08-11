using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubVora.Application.Auth;
using SubVora.Application.Dashboard;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Api.Tests;

public class DashboardControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public DashboardControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        const string password = "correct-horse-battery-staple";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetBurnRate_WithActiveMonthlySubscription_ReturnsCorrectTotals()
    {
        var client = await CreateAuthenticatedClientAsync($"dash-{Guid.NewGuid()}@example.com");
        var request = new CreateSubscriptionRequest
        {
            CustomName = "Netflix",
            CostAmount = 30m,
            // Matches the registration default home currency, so this asserts burn-rate math
            // without dragging live FX conversion into it.
            Currency = "INR",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 8, 1),
            AlertDaysAdvance = 3,
        };
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var response = await client.GetAsync("/api/v1/dashboard/burn-rate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BurnRateResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(30m, result!.Monthly);
    }

    [Fact]
    public async Task GetBurnRate_GroupsSpendByThePaymentSourceItIsChargedTo()
    {
        var client = await CreateAuthenticatedClientAsync($"dash-source-{Guid.NewGuid()}@example.com");

        var cardResponse = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = "HDFC Card", SourceType = PaymentSourceType.Card }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        var card = await cardResponse.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions);

        await CreateSubscriptionAsync(client, "Netflix", 30m, card!.Id);
        await CreateSubscriptionAsync(client, "Spotify", 20m, card.Id);
        // No payment source, so it must land in its own "Unassigned" bucket rather than silently
        // padding the card's total.
        await CreateSubscriptionAsync(client, "Gym", 10m, paymentSourceId: null);

        var response = await client.GetAsync("/api/v1/dashboard/burn-rate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BurnRateResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(2, result!.ByPaymentSource.Count);
        Assert.Equal("HDFC Card", result.ByPaymentSource[0].PaymentSourceLabel);
        Assert.Equal(card.Id, result.ByPaymentSource[0].PaymentSourceId);
        Assert.Equal(50m, result.ByPaymentSource[0].MonthlyAmount);
        Assert.Equal("Unassigned", result.ByPaymentSource[1].PaymentSourceLabel);
        Assert.Null(result.ByPaymentSource[1].PaymentSourceId);
        Assert.Equal(10m, result.ByPaymentSource[1].MonthlyAmount);
    }

    private static async Task CreateSubscriptionAsync(HttpClient client, string name, decimal cost, Guid? paymentSourceId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            CustomName = name,
            CostAmount = cost,
            Currency = "INR",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 8, 1),
            AlertDaysAdvance = 3,
            PaymentSourceId = paymentSourceId,
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetBurnRate_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/dashboard/burn-rate");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
