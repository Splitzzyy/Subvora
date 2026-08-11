using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubVora.Application.Auth;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Api.Tests;

/// <summary>
/// Marking a charge paid is the only thing that moves next_billing_date now that the nightly
/// advance job is gone - so a date left in the past means the charge is genuinely outstanding.
/// </summary>
public class MarkPaidControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public MarkPaidControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"markpaid-{Guid.NewGuid()}@example.com";
        // Throwaway literal for a throwaway account in an ephemeral Testcontainers database, the
        // same one every other controller test here registers with.
        const string password = "correct-horse-battery-staple"; // pragma: allowlist secret

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<SubscriptionDto> CreateAsync(
        HttpClient client, BillingCycleType cadence, DateOnly purchase, DateOnly nextBilling)
    {
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            CustomName = "Netflix",
            CostAmount = 299m,
            Currency = "INR",
            CycleCadence = cadence,
            PurchaseDate = purchase,
            NextBillingDate = nextBilling,
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions))!;
    }

    [Fact]
    public async Task MarkPaid_SettlesTheOutstandingDateAndAdvancesOneCycleFromIt()
    {
        var client = await CreateAuthenticatedClientAsync();
        var due = new DateOnly(2026, 4, 23);
        var created = await CreateAsync(client, BillingCycleType.Monthly, new DateOnly(2026, 3, 23), due);
        Assert.Null(created.LastPaidDate);

        var response = await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settled = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(due, settled!.LastPaidDate);
        // A cycle on from the date settled, not from today - anything else forgives the periods
        // between, which is exactly what the removed nightly job used to do.
        Assert.Equal(new DateOnly(2026, 5, 23), settled.NextBillingDate);
        Assert.True(settled.IsActive);
    }

    [Fact]
    public async Task MarkPaid_OnAQuarterlySubscription_AdvancesThreeCalendarMonths()
    {
        var client = await CreateAuthenticatedClientAsync();
        // 30 Nov, so the clamp is exercised on the way through: three months lands on 28 Feb, and
        // a day-count cycle would have put it on 1 March.
        var due = new DateOnly(2025, 11, 30);
        var created = await CreateAsync(client, BillingCycleType.Quarterly, new DateOnly(2025, 8, 30), due);

        var response = await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settled = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(due, settled!.LastPaidDate);
        Assert.Equal(new DateOnly(2026, 2, 28), settled.NextBillingDate);
        Assert.True(settled.IsActive);
    }

    [Fact]
    public async Task MarkPaid_OnAOneTimeSubscription_EndsItRatherThanBillingAgain()
    {
        var client = await CreateAuthenticatedClientAsync();
        var due = new DateOnly(2026, 4, 23);
        var created = await CreateAsync(client, BillingCycleType.OneTime, due, due);

        var response = await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);

        var settled = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(due, settled!.LastPaidDate);
        Assert.Equal(due, settled.NextBillingDate);
        Assert.False(settled.IsActive);
    }

    [Fact]
    public async Task MarkPaid_TwiceInARow_AdvancesTwoCycles()
    {
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client, BillingCycleType.Monthly, new DateOnly(2026, 3, 23), new DateOnly(2026, 4, 23));

        await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);
        var second = await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);

        var settled = await second.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        // Someone clearing a backlog pays one period per tap; skipping to "now" would write off
        // charges they never settled.
        Assert.Equal(new DateOnly(2026, 5, 23), settled!.LastPaidDate);
        Assert.Equal(new DateOnly(2026, 6, 23), settled.NextBillingDate);
    }

    [Fact]
    public async Task MarkPaid_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync($"/api/v1/subscriptions/{Guid.NewGuid()}/mark-paid", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkPaid_SomeoneElsesSubscription_Returns404()
    {
        var owner = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(owner, BillingCycleType.Monthly, new DateOnly(2026, 3, 23), new DateOnly(2026, 4, 23));

        var stranger = await CreateAuthenticatedClientAsync();
        var response = await stranger.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
