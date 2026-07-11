using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application.Auth;
using SubVora.Application.Subscriptions;
using SubVora.Domain.Enums;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

public class SubscriptionsControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    // The server serializes enums as strings (JsonStringEnumConverter, configured in
    // Program.cs); HttpContentJsonExtensions defaults don't pick that up automatically.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public SubscriptionsControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        const string password = "correct-horse-battery-staple";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

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
        Currency = "USD",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 8, 1),
        AlertDaysAdvance = 3,
        IsFreeTrial = false,
    };

    [Fact]
    public async Task CreateSubscription_WithValidPayload_Returns201AndPersistsRecord()
    {
        var client = await CreateAuthenticatedClientAsync($"create-sub-{Guid.NewGuid()}@example.com");
        var request = ValidRequest();

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto!.Id);
        Assert.Equal(request.CustomName, dto.CustomName);
        Assert.Equal(request.CostAmount, dto.CostAmount);
        Assert.Equal(request.Currency, dto.Currency);
        Assert.Equal(request.CycleCadence, dto.CycleCadence);
        Assert.True(dto.IsActive);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.UserSubscriptions.AsNoTracking().SingleAsync(s => s.Id == dto.Id);
        Assert.Equal(request.CustomName, stored.CustomName);
        Assert.NotEqual(Guid.Empty, stored.UserId);
    }

    [Fact]
    public async Task CreateSubscription_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithInvalidCurrency_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"create-sub-badcurrency-{Guid.NewGuid()}@example.com");
        var request = ValidRequest();
        request.Currency = "XXX";

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithNonPositiveCost_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"create-sub-badcost-{Guid.NewGuid()}@example.com");
        var request = ValidRequest();
        request.CostAmount = 0m;

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithNonPositiveAlertDaysAdvance_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"create-sub-badalert-{Guid.NewGuid()}@example.com");
        var request = ValidRequest();
        request.AlertDaysAdvance = 0;

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_WithNextBillingDateBeforePurchaseDate_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"create-sub-baddates-{Guid.NewGuid()}@example.com");
        var request = ValidRequest();
        request.PurchaseDate = new DateOnly(2026, 8, 1);
        request.NextBillingDate = new DateOnly(2026, 1, 1);

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
