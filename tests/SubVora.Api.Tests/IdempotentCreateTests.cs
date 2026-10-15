using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubVora.Application.Auth;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;

namespace SubVora.Api.Tests;

/// <summary>
/// A create whose response is lost (a cold start outlasting the client timeout, a dropped
/// connection) used to leave the user tapping Save again and getting two records. A client-chosen
/// id turns that retry into a lookup of the record the first attempt made.
/// </summary>
public class IdempotentCreateTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public IdempotentCreateTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"idempotent-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple"; // pragma: allowlist secret

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static CreateSubscriptionRequest Subscription(Guid? id) => new()
    {
        Id = id,
        CustomName = "Netflix",
        CostAmount = 299m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 11, 1),
    };

    private static async Task<int> CountSubscriptionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<SubscriptionDto>>("/api/v1/subscriptions", JsonOptions))!.Count;

    private static async Task<int> CountPaymentSourcesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PaymentSourceDto>>("/api/v1/payment-sources", JsonOptions))!.Count;

    [Fact]
    public async Task CreateSubscription_SameIdTwice_CreatesOne_AndReplaysTheFirst()
    {
        var client = await CreateAuthenticatedClientAsync();
        var id = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions);
        var retry = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        var replayed = await retry.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(id, created!.Id);
        Assert.Equal(id, replayed!.Id);
        Assert.Equal(created.Version, replayed.Version);
        Assert.Equal(1, await CountSubscriptionsAsync(client));
    }

    [Fact]
    public async Task CreateSubscription_SameIdConcurrently_CreatesOne()
    {
        var client = await CreateAuthenticatedClientAsync();
        var id = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Got {(int)r.StatusCode}."));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, await CountSubscriptionsAsync(client));
    }

    [Fact]
    public async Task CreateSubscription_WithAnotherUsersId_Returns409_AndRevealsNothing()
    {
        var owner = await CreateAuthenticatedClientAsync();
        var other = await CreateAuthenticatedClientAsync();
        var id = Guid.NewGuid();
        await owner.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions);

        var response = await other.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Netflix", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountSubscriptionsAsync(other));
    }

    [Fact]
    public async Task CreateSubscription_WithEmptyId_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(Guid.Empty), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithoutId_StillCreatesEachTime()
    {
        // Already-installed APKs send no id; they keep today's behaviour.
        var client = await CreateAuthenticatedClientAsync();

        var first = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(null), JsonOptions);
        var second = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(null), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(2, await CountSubscriptionsAsync(client));
    }

    [Fact]
    public async Task UpdateSubscription_IgnoresTheIdInTheBody()
    {
        var client = await CreateAuthenticatedClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(null), JsonOptions))
            .Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        var edit = Subscription(Guid.NewGuid());
        edit.CustomName = "Netflix Premium";

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", edit, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal("Netflix Premium", updated.CustomName);
        Assert.Equal(1, await CountSubscriptionsAsync(client));
    }

    [Fact]
    public async Task CreatePaymentSource_SameIdTwice_CreatesOne_AndReplaysTheFirst()
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = new CreatePaymentSourceRequest { Id = Guid.NewGuid(), Label = "HDFC Visa", SourceType = PaymentSourceType.Card };

        var first = await client.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions);
        var retry = await client.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(request.Id, (await retry.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions))!.Id);
        Assert.Equal(1, await CountPaymentSourcesAsync(client));
    }

    [Fact]
    public async Task CreatePaymentSource_SameIdConcurrently_CreatesOne()
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = new CreatePaymentSourceRequest { Id = Guid.NewGuid(), Label = "UPI", SourceType = PaymentSourceType.Wallet };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Got {(int)r.StatusCode}."));
        Assert.Equal(1, await CountPaymentSourcesAsync(client));
    }

    [Fact]
    public async Task CreatePaymentSource_WithAnotherUsersId_Returns409()
    {
        var owner = await CreateAuthenticatedClientAsync();
        var other = await CreateAuthenticatedClientAsync();
        var id = Guid.NewGuid();
        await owner.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Id = id, Label = "Amex" }, JsonOptions);

        var response = await other.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Id = id, Label = "Mine" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountPaymentSourcesAsync(other));
    }

    [Fact]
    public async Task CreatePaymentSource_WithEmptyId_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Id = Guid.Empty, Label = "Cash" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
