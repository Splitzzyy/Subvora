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
/// A subscription's category_id/payment_source_id/catalog_id foreign keys only require the row to
/// exist, so ownership is the API's job. These pin that a caller cannot reference - or read back -
/// another user's rows.
/// </summary>
public class CrossUserReferenceTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public CrossUserReferenceTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string prefix)
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";  // pragma: allowlist secret

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
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
        IsFreeTrial = false,
    };

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions);
        return dto!.Id;
    }

    private static async Task<Guid> CreatePaymentSourceAsync(HttpClient client, string label)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/payment-sources",
            new CreatePaymentSourceRequest { Label = label, SourceType = PaymentSourceType.Card },
            JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions);
        return dto!.Id;
    }

    [Fact]
    public async Task CreateSubscription_WithAnotherUsersPaymentSource_Returns400()
    {
        var victimClient = await CreateAuthenticatedClientAsync("xref-ps-victim");
        var attackerClient = await CreateAuthenticatedClientAsync("xref-ps-attacker");

        var victimPaymentSourceId = await CreatePaymentSourceAsync(victimClient, "HDFC 4471");

        var request = ValidRequest();
        request.PaymentSourceId = victimPaymentSourceId;

        var response = await attackerClient.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithAnotherUsersCategory_Returns400()
    {
        var victimClient = await CreateAuthenticatedClientAsync("xref-cat-victim");
        var attackerClient = await CreateAuthenticatedClientAsync("xref-cat-attacker");

        var victimCategoryId = await CreateCategoryAsync(victimClient, $"Private {Guid.NewGuid()}");

        var request = ValidRequest();
        request.CategoryId = victimCategoryId;

        var response = await attackerClient.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithSystemDefaultCategory_Returns201()
    {
        var client = await CreateAuthenticatedClientAsync("xref-syscat");

        // Seeded by the SeedSystemCategories migration, owned by nobody - shared on purpose, and
        // the case a blunt "must be mine" check would wrongly reject.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var systemCategoryId = await dbContext.Categories.AsNoTracking()
            .Where(c => c.UserId == null)
            .Select(c => c.Id)
            .FirstAsync();

        var request = ValidRequest();
        request.CategoryId = systemCategoryId;

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(systemCategoryId, dto!.CategoryId);
        Assert.NotNull(dto.CategoryName);
    }

    [Fact]
    public async Task CreateSubscription_WithOwnCategoryAndPaymentSource_Returns201()
    {
        var client = await CreateAuthenticatedClientAsync("xref-own");

        var categoryId = await CreateCategoryAsync(client, $"Mine {Guid.NewGuid()}");
        var paymentSourceId = await CreatePaymentSourceAsync(client, "My Amex");

        var request = ValidRequest();
        request.CategoryId = categoryId;
        request.PaymentSourceId = paymentSourceId;

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(categoryId, dto!.CategoryId);
        Assert.Equal(paymentSourceId, dto.PaymentSourceId);
        Assert.Equal("My Amex", dto.PaymentSourceLabel);
    }

    [Fact]
    public async Task UpdateSubscription_WithAnotherUsersPaymentSource_Returns400()
    {
        var victimClient = await CreateAuthenticatedClientAsync("xref-upd-victim");
        var attackerClient = await CreateAuthenticatedClientAsync("xref-upd-attacker");

        var victimPaymentSourceId = await CreatePaymentSourceAsync(victimClient, "Mum's Amex");

        var createResponse = await attackerClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var request = ValidRequest();
        request.PaymentSourceId = victimPaymentSourceId;

        var response = await attackerClient.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptions_DoesNotDiscloseAnotherUsersPaymentSourceLabel()
    {
        var victimClient = await CreateAuthenticatedClientAsync("xref-read-victim");
        var attackerClient = await CreateAuthenticatedClientAsync("xref-read-attacker");

        var victimPaymentSourceId = await CreatePaymentSourceAsync(victimClient, "ICICI 9902");
        var victimCategoryId = await CreateCategoryAsync(victimClient, $"Therapy {Guid.NewGuid()}");

        var createResponse = await attackerClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        // Writes the cross-tenant reference straight to the database, bypassing the controller -
        // this is the state a row created before the ownership check would be in, and the read path
        // has to stay safe on its own.
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await dbContext.UserSubscriptions.SingleAsync(s => s.Id == created!.Id);
            subscription.PaymentSourceId = victimPaymentSourceId;
            subscription.CategoryId = victimCategoryId;
            await dbContext.SaveChangesAsync();
        }

        var listResponse = await attackerClient.GetAsync("/api/v1/subscriptions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<SubscriptionDto>>(JsonOptions);

        var leaked = list!.Single(s => s.Id == created!.Id);
        Assert.Null(leaked.PaymentSourceLabel);
        Assert.Null(leaked.CategoryName);
    }
}
