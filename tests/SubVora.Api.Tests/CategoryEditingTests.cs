using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application.Auth;
using SubVora.Application.Categories;
using SubVora.Application.PaymentSources;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

/// <summary>
/// Renaming and deleting the things a user creates. Both were missing entirely: a typo'd category
/// was permanent, and a payment source could only be deleted, which detaches every subscription
/// pointing at it.
/// </summary>
public class CategoryEditingTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public CategoryEditingTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"catedit-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";  // pragma: allowlist secret

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<CategoryDto> CreateCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;
    }

    private static async Task<SubscriptionDto> CreateSubscriptionAsync(HttpClient client, Guid? categoryId = null, Guid? paymentSourceId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", new CreateSubscriptionRequest
        {
            CustomName = "Netflix",
            CostAmount = 19.99m,
            Currency = "INR",
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 8, 1),
            CategoryId = categoryId,
            PaymentSourceId = paymentSourceId,
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions))!;
    }

    [Fact]
    public async Task RenameCategory_ChangesTheNameEverywhereItIsShown()
    {
        var client = await CreateAuthenticatedClientAsync();
        var category = await CreateCategoryAsync(client, $"Entertainmnet {Guid.NewGuid()}");
        var subscription = await CreateSubscriptionAsync(client, categoryId: category.Id);

        var corrected = $"Entertainment {Guid.NewGuid()}";
        var response = await client.PutAsJsonAsync($"/api/v1/categories/{category.Id}", new CreateCategoryRequest { Name = corrected }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The id never moves, so the subscription keeps its category and simply reads correctly now.
        var refetched = await client.GetFromJsonAsync<SubscriptionDto>($"/api/v1/subscriptions/{subscription.Id}", JsonOptions);
        Assert.Equal(corrected, refetched!.CategoryName);
    }

    [Fact]
    public async Task RenameCategory_ToANameAlreadyInUse_Returns409()
    {
        var client = await CreateAuthenticatedClientAsync();
        var taken = $"Streaming {Guid.NewGuid()}";
        await CreateCategoryAsync(client, taken);
        var other = await CreateCategoryAsync(client, $"Music {Guid.NewGuid()}");

        var response = await client.PutAsJsonAsync($"/api/v1/categories/{other.Id}", new CreateCategoryRequest { Name = taken }, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RenameCategory_ForASystemDefault_Returns404()
    {
        // Shared by every account on the instance. Seeing one is not owning it, and renaming it
        // would change it for everybody.
        var client = await CreateAuthenticatedClientAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var systemCategoryId = await dbContext.Categories.AsNoTracking().Where(c => c.UserId == null).Select(c => c.Id).FirstAsync();

        var response = await client.PutAsJsonAsync($"/api/v1/categories/{systemCategoryId}", new CreateCategoryRequest { Name = "Mine now" }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var unchanged = await dbContext.Categories.AsNoTracking().SingleAsync(c => c.Id == systemCategoryId);
        Assert.NotEqual("Mine now", unchanged.Name);
    }

    [Fact]
    public async Task RenameCategory_ForAnotherUsersCategory_Returns404()
    {
        var victim = await CreateAuthenticatedClientAsync();
        var attacker = await CreateAuthenticatedClientAsync();
        var victimCategory = await CreateCategoryAsync(victim, $"Private {Guid.NewGuid()}");

        var response = await attacker.PutAsJsonAsync($"/api/v1/categories/{victimCategory.Id}", new CreateCategoryRequest { Name = "Renamed" }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_KeepsTheSubscriptionsAndReportsHowManyItUncategorised()
    {
        // ON DELETE SET NULL: losing a grouping beats losing the record, but the user is entitled
        // to know how much moved.
        var client = await CreateAuthenticatedClientAsync();
        var category = await CreateCategoryAsync(client, $"Doomed {Guid.NewGuid()}");
        var first = await CreateSubscriptionAsync(client, categoryId: category.Id);
        var second = await CreateSubscriptionAsync(client, categoryId: category.Id);

        var response = await client.DeleteAsync($"/api/v1/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DeleteCategoryResult>(JsonOptions);
        Assert.Equal(2, result!.SubscriptionsUncategorized);

        foreach (var id in new[] { first.Id, second.Id })
        {
            var survivor = await client.GetFromJsonAsync<SubscriptionDto>($"/api/v1/subscriptions/{id}", JsonOptions);
            Assert.NotNull(survivor);
            Assert.Null(survivor!.CategoryId);
            Assert.Null(survivor.CategoryName);
        }
    }

    [Fact]
    public async Task DeleteCategory_RemovesItFromTheList()
    {
        var client = await CreateAuthenticatedClientAsync();
        var category = await CreateCategoryAsync(client, $"Temporary {Guid.NewGuid()}");

        await client.DeleteAsync($"/api/v1/categories/{category.Id}");

        var remaining = await client.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories", JsonOptions);
        Assert.DoesNotContain(remaining!, c => c.Id == category.Id);
    }

    [Fact]
    public async Task DeleteCategory_ForASystemDefault_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var systemCategoryId = await dbContext.Categories.AsNoTracking().Where(c => c.UserId == null).Select(c => c.Id).FirstAsync();

        var response = await client.DeleteAsync($"/api/v1/categories/{systemCategoryId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await dbContext.Categories.AsNoTracking().AnyAsync(c => c.Id == systemCategoryId));
    }

    [Fact]
    public async Task DeleteCategory_ForAnotherUsersCategory_Returns404AndLeavesItAlone()
    {
        var victim = await CreateAuthenticatedClientAsync();
        var attacker = await CreateAuthenticatedClientAsync();
        var victimCategory = await CreateCategoryAsync(victim, $"Private {Guid.NewGuid()}");

        var response = await attacker.DeleteAsync($"/api/v1/categories/{victimCategory.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stillThere = await victim.GetFromJsonAsync<List<CategoryDto>>("/api/v1/categories", JsonOptions);
        Assert.Contains(stillThere!, c => c.Id == victimCategory.Id);
    }

    [Fact]
    public async Task UpdatePaymentSource_RenamesInPlaceAndKeepsSubscriptionsAttached()
    {
        // The reason this exists rather than delete-and-recreate: the foreign key is SET NULL, so
        // recreating would leave every subscription that used it with no payment source.
        var client = await CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest
        {
            Label = "HDFC 4021",
            SourceType = PaymentSourceType.Card,
        }, JsonOptions);
        var paymentSource = (await created.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions))!;
        var subscription = await CreateSubscriptionAsync(client, paymentSourceId: paymentSource.Id);

        var response = await client.PutAsJsonAsync($"/api/v1/payment-sources/{paymentSource.Id}", new CreatePaymentSourceRequest
        {
            Label = "HDFC Credit ••4021",
            SourceType = PaymentSourceType.Card,
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refetched = await client.GetFromJsonAsync<SubscriptionDto>($"/api/v1/subscriptions/{subscription.Id}", JsonOptions);
        Assert.Equal(paymentSource.Id, refetched!.PaymentSourceId);
        Assert.Equal("HDFC Credit ••4021", refetched.PaymentSourceLabel);
    }

    [Fact]
    public async Task UpdatePaymentSource_ForAnotherUsersSource_Returns404()
    {
        var victim = await CreateAuthenticatedClientAsync();
        var attacker = await CreateAuthenticatedClientAsync();

        var created = await victim.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest
        {
            Label = "Mum's Amex",
            SourceType = PaymentSourceType.Card,
        }, JsonOptions);
        var victimSource = (await created.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions))!;

        var response = await attacker.PutAsJsonAsync($"/api/v1/payment-sources/{victimSource.Id}", new CreatePaymentSourceRequest
        {
            Label = "Mine now",
            SourceType = PaymentSourceType.Wallet,
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePaymentSource_WithAnEmptyLabel_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync();
        var created = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest
        {
            Label = "Visa",
            SourceType = PaymentSourceType.Card,
        }, JsonOptions);
        var paymentSource = (await created.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions))!;

        var response = await client.PutAsJsonAsync($"/api/v1/payment-sources/{paymentSource.Id}", new CreatePaymentSourceRequest
        {
            Label = "",
            SourceType = PaymentSourceType.Card,
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
