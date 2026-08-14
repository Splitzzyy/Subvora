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
/// An update carrying the version it was made against must not overwrite a row that has moved on.
/// The case that matters is an edit screen opened before a mark-paid: applying it writes the
/// pre-payment billing date back, so the charge is settled and outstanding at once.
/// </summary>
public class ConcurrentUpdateTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public ConcurrentUpdateTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"concurrency-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";  // pragma: allowlist secret

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static CreateSubscriptionRequest ValidRequest() => new()
    {
        CustomName = "Netflix Premium",
        CostAmount = 19.99m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 8, 1),
        AlertDaysAdvance = 3,
    };

    private static async Task<SubscriptionDto> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions))!;
    }

    [Fact]
    public async Task Subscriptions_CarryAVersion()
    {
        var client = await CreateAuthenticatedClientAsync();

        var created = await CreateAsync(client);

        Assert.NotEqual(0u, created.Version);
    }

    [Fact]
    public async Task Update_WithTheVersionItWasReadAt_Succeeds()
    {
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client);

        var request = ValidRequest();
        request.Version = created.Version;
        request.CustomName = "Netflix Standard";

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", request, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal("Netflix Standard", updated!.CustomName);

        // A new version, or a second save with the stale one would still be accepted.
        Assert.NotEqual(created.Version, updated.Version);
    }

    [Fact]
    public async Task Update_WithAStaleVersionAfterAMarkPaid_Returns409AndLeavesThePaymentIntact()
    {
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client);

        // The edit screen opened here, holding created.Version.
        var markPaid = await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);
        Assert.Equal(HttpStatusCode.OK, markPaid.StatusCode);
        var paid = await markPaid.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        // Saving that screen now would write the pre-payment billing date back.
        var request = ValidRequest();
        request.Version = created.Version;
        request.NextBillingDate = created.NextBillingDate;

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var afterResponse = await client.GetAsync($"/api/v1/subscriptions/{created.Id}");
        var after = await afterResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(paid!.NextBillingDate, after!.NextBillingDate);
        Assert.Equal(paid.LastPaidDate, after.LastPaidDate);
    }

    [Fact]
    public async Task Update_WithAStaleVersionAfterAnotherEdit_Returns409()
    {
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client);

        var firstEdit = ValidRequest();
        firstEdit.Version = created.Version;
        firstEdit.CustomName = "Edited on device A";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", firstEdit, JsonOptions)).StatusCode);

        var secondEdit = ValidRequest();
        secondEdit.Version = created.Version;
        secondEdit.CustomName = "Edited on device B";

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", secondEdit, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var afterResponse = await client.GetAsync($"/api/v1/subscriptions/{created.Id}");
        var after = await afterResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal("Edited on device A", after!.CustomName);
    }

    [Fact]
    public async Task Update_WithAStaleVersionButNoActualChanges_StillReturns409()
    {
        // Every other conflict test alters a field, so EF generates an UPDATE and the xmin predicate
        // rides along with it. Submit values byte-identical to what is stored and EF marks nothing
        // modified, issues no statement at all, and SaveChangesAsync cannot raise a concurrency
        // exception - the save answered 200 against a row that had moved on.
        //
        // This is the shape the check exists for, not a corner case: a user who opened the edit
        // screen, changed nothing, and pressed Save is exactly the one whose write would silently
        // roll back whatever happened underneath them.
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client);

        var firstEdit = ValidRequest();
        firstEdit.Version = created.Version;
        firstEdit.CustomName = "Edited on device A";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", firstEdit, JsonOptions)).StatusCode);

        // Exactly what is stored now, but carrying the version read before device A's edit.
        var unchanged = ValidRequest();
        unchanged.Version = created.Version;
        unchanged.CustomName = "Edited on device A";

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", unchanged, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithTheCurrentVersionAndNoActualChanges_StillSucceeds()
    {
        // The other side of the same guard: forcing the write must not turn an unchanged save
        // carrying a *current* version into a spurious conflict.
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client);

        var unchanged = ValidRequest();
        unchanged.Version = created.Version;

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", unchanged, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNoVersion_StillAppliesUnconditionally()
    {
        // Backward compatibility: APKs already installed do not know about Version, and sideloaded
        // builds are not force-upgraded. Omitting it keeps the previous last-write-wins behaviour
        // rather than making every old client's saves fail.
        var client = await CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client);

        await client.PostAsync($"/api/v1/subscriptions/{created.Id}/mark-paid", null);

        var request = ValidRequest();
        request.Version = null;
        request.CustomName = "Saved by an older client";

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", request, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithAVersionButNoSuchSubscription_Returns404NotConflict()
    {
        var client = await CreateAuthenticatedClientAsync();

        var request = ValidRequest();
        request.Version = 12345u;

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{Guid.NewGuid()}", request, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
