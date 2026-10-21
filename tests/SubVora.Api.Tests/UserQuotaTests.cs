using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application;
using SubVora.Application.Auth;
using SubVora.Application.Categories;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Entities;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

/// <summary>
/// Every account shares one small database, so each create is capped per account. The caps are
/// seeded straight into the database - creating 500 subscriptions through the API would only test
/// patience.
/// </summary>
public class UserQuotaTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Password = "Correct-horse-battery-staple1"; // pragma: allowlist secret

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public UserQuotaTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid UserId)> SignedInAsync()
    {
        var client = _factory.CreateClient();
        var email = $"quota-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = Password });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return (client, await WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()));
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task SeedAsync(Func<int, object> row, int count) =>
        WithDbAsync(db =>
        {
            db.AddRange(Enumerable.Range(0, count).Select(row));
            return db.SaveChangesAsync();
        });

    private static CreateSubscriptionRequest Subscription(Guid? id = null) => new()
    {
        Id = id,
        CustomName = "One too many",
        CostAmount = 1m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 11, 1),
    };

    private Task SeedSubscriptionsAsync(Guid userId, int count) => SeedAsync(i => new UserSubscription
    {
        UserId = userId,
        CustomName = $"Seeded {i}",
        CostAmount = 1m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 11, 1),
        AlertDaysAdvance = 3,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    }, count);

    private static async Task AssertQuotaRejectionAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateSubscription_AtTheCap_Returns400_AndAnotherUserIsUnaffected()
    {
        var (client, userId) = await SignedInAsync();
        await SeedSubscriptionsAsync(userId, UserQuotas.Subscriptions);
        var (other, _) = await SignedInAsync();

        await AssertQuotaRejectionAsync(
            await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(), JsonOptions),
            $"You can have at most {UserQuotas.Subscriptions} subscriptions.");
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/v1/subscriptions", Subscription(), JsonOptions)).StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_BelowTheCap_StillCreates()
    {
        var (client, userId) = await SignedInAsync();
        await SeedSubscriptionsAsync(userId, UserQuotas.Subscriptions - 1);

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_ReplayAtTheCap_StillReturnsTheOriginal()
    {
        // The replayed create made the row that filled the quota; refusing its retry would turn a
        // lost response into an error for something that in fact succeeded.
        var (client, userId) = await SignedInAsync();
        await SeedSubscriptionsAsync(userId, UserQuotas.Subscriptions - 1);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions)).StatusCode);

        var replay = await client.PostAsJsonAsync("/api/v1/subscriptions", Subscription(id), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_AtTheCap_Returns400_AndSystemDefaultsDoNotCount()
    {
        // The eight system defaults are visible to every account and not counted against it.
        var (client, userId) = await SignedInAsync();
        await SeedAsync(i => new Category { UserId = userId, Name = $"Seeded {i}", CreatedAt = DateTimeOffset.UtcNow }, UserQuotas.Categories - 1);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = "Last one" })).StatusCode);

        await AssertQuotaRejectionAsync(
            await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = "One too many" }),
            $"You can have at most {UserQuotas.Categories} categories of your own.");
    }

    [Fact]
    public async Task CreateCategory_DuplicateNameAtTheCap_Returns409()
    {
        // A retried create whose response was lost: the honest answer is the same "already exists"
        // it gets below the cap, not a quota error for a row that is already there.
        var (client, userId) = await SignedInAsync();
        await SeedAsync(i => new Category { UserId = userId, Name = $"Seeded {i}", CreatedAt = DateTimeOffset.UtcNow }, UserQuotas.Categories - 1);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = "Gym" })).StatusCode);

        var retry = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = "Gym" });

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    [Fact]
    public async Task CreatePaymentSource_ReplayAtTheCap_StillReturnsTheOriginal()
    {
        var (client, userId) = await SignedInAsync();
        await SeedAsync(i => new PaymentSource { UserId = userId, Label = $"Card {i}", SourceType = PaymentSourceType.Card, CreatedAt = DateTimeOffset.UtcNow }, UserQuotas.PaymentSources - 1);
        var request = new CreatePaymentSourceRequest { Id = Guid.NewGuid(), Label = "Last one", SourceType = PaymentSourceType.Card };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions)).StatusCode);

        var replay = await client.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task CreatePaymentSource_AtTheCap_Returns400_AndAnotherUserIsUnaffected()
    {
        var (client, userId) = await SignedInAsync();
        await SeedAsync(i => new PaymentSource { UserId = userId, Label = $"Card {i}", SourceType = PaymentSourceType.Card, CreatedAt = DateTimeOffset.UtcNow }, UserQuotas.PaymentSources);
        var (other, _) = await SignedInAsync();
        var request = new CreatePaymentSourceRequest { Label = "One too many", SourceType = PaymentSourceType.Card };

        await AssertQuotaRejectionAsync(
            await client.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions),
            $"You can have at most {UserQuotas.PaymentSources} payment sources.");
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/v1/payment-sources", request, JsonOptions)).StatusCode);
    }
}
