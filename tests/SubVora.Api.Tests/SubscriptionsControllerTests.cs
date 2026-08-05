using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application.Auth;
using SubVora.Application.Dashboard;
using SubVora.Application.Matching;
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

    [Fact]
    public async Task GetSubscriptions_ReturnsOnlyCallersSubscriptions()
    {
        var ownerClient = await CreateAuthenticatedClientAsync($"list-owner-{Guid.NewGuid()}@example.com");
        var otherClient = await CreateAuthenticatedClientAsync($"list-other-{Guid.NewGuid()}@example.com");

        var ownerCreate = await ownerClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, ownerCreate.StatusCode);
        var ownerDto = await ownerCreate.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var otherCreate = await otherClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.Created, otherCreate.StatusCode);

        var listResponse = await ownerClient.GetAsync("/api/v1/subscriptions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<SubscriptionDto>>(JsonOptions);

        Assert.NotNull(list);
        Assert.Contains(list!, s => s.Id == ownerDto!.Id);
        Assert.All(list!, s => Assert.NotEqual(default, s.Id));
        Assert.DoesNotContain(list!, s => s.CustomName == "Netflix Premium" && s.Id != ownerDto!.Id);
    }

    [Fact]
    public async Task GetSubscriptions_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionById_ReturnsOwnRecord()
    {
        var client = await CreateAuthenticatedClientAsync($"getbyid-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var getResponse = await client.GetAsync($"/api/v1/subscriptions/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var dto = await getResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal(created.Id, dto!.Id);
        Assert.Equal(created.CustomName, dto.CustomName);
    }

    [Fact]
    public async Task GetSubscriptionById_ForAnotherUsersRecord_Returns404()
    {
        var ownerClient = await CreateAuthenticatedClientAsync($"getbyid-owner-{Guid.NewGuid()}@example.com");
        var attackerClient = await CreateAuthenticatedClientAsync($"getbyid-attacker-{Guid.NewGuid()}@example.com");

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var getResponse = await attackerClient.GetAsync($"/api/v1/subscriptions/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionById_NonexistentId_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync($"getbyid-missing-{Guid.NewGuid()}@example.com");

        var response = await client.GetAsync($"/api/v1/subscriptions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionById_ResolvesCategoryNamePaymentSourceLabelAndCatalogLogoUrl()
    {
        var email = $"getbyid-resolved-{Guid.NewGuid()}@example.com";
        var client = await CreateAuthenticatedClientAsync(email);

        Guid userId;
        Guid categoryId;
        Guid paymentSourceId;
        Guid catalogId;
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await dbContext.Users.AsNoTracking().SingleAsync(u => u.Email == email);
            userId = user.Id;

            var category = new SubVora.Domain.Entities.Category { UserId = userId, Name = $"Streaming-{Guid.NewGuid()}", CreatedAt = DateTimeOffset.UtcNow };
            var paymentSource = new SubVora.Domain.Entities.PaymentSource { UserId = userId, Label = "Visa •9999", SourceType = PaymentSourceType.Card, CreatedAt = DateTimeOffset.UtcNow };
            var catalogItem = new SubVora.Domain.Entities.SubscriptionCatalogItem { ProviderName = $"Netflix-{Guid.NewGuid()}", LogoUrl = "https://example.com/netflix.png", CreatedAt = DateTimeOffset.UtcNow };
            dbContext.AddRange(category, paymentSource, catalogItem);
            await dbContext.SaveChangesAsync();
            categoryId = category.Id;
            paymentSourceId = paymentSource.Id;
            catalogId = catalogItem.Id;
        }

        var request = ValidRequest();
        request.CategoryId = categoryId;
        request.PaymentSourceId = paymentSourceId;
        request.CatalogId = catalogId;
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var getResponse = await client.GetAsync($"/api/v1/subscriptions/{created!.Id}");
        var dto = await getResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.StartsWith("Streaming-", dto!.CategoryName);
        Assert.Equal("Visa •9999", dto.PaymentSourceLabel);
        Assert.Equal("https://example.com/netflix.png", dto.CatalogLogoUrl);
    }

    [Fact]
    public async Task UpdateSubscription_ChangesPersist()
    {
        var client = await CreateAuthenticatedClientAsync($"update-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var updateRequest = ValidRequest();
        updateRequest.CustomName = "Netflix Standard";
        updateRequest.CostAmount = 12.49m;
        updateRequest.Currency = "EUR";

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", updateRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal("Netflix Standard", updated!.CustomName);
        Assert.Equal(12.49m, updated.CostAmount);
        Assert.Equal("EUR", updated.Currency);

        var getResponse = await client.GetAsync($"/api/v1/subscriptions/{created.Id}");
        var reloaded = await getResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.Equal("Netflix Standard", reloaded!.CustomName);
    }

    [Fact]
    public async Task UpdateSubscription_ForAnotherUsersRecord_Returns404()
    {
        var ownerClient = await CreateAuthenticatedClientAsync($"update-owner-{Guid.NewGuid()}@example.com");
        var attackerClient = await CreateAuthenticatedClientAsync($"update-attacker-{Guid.NewGuid()}@example.com");

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var updateResponse = await attackerClient.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", ValidRequest(), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateSubscription_WithInvalidPayload_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"update-invalid-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var invalidUpdate = ValidRequest();
        invalidUpdate.CostAmount = -5m;

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", invalidUpdate, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateSubscription_NonexistentId_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync($"update-missing-{Guid.NewGuid()}@example.com");

        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{Guid.NewGuid()}", ValidRequest(), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSubscription_RemovesRecord()
    {
        var client = await CreateAuthenticatedClientAsync($"delete-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/v1/subscriptions/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/subscriptions/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/subscriptions");
        var list = await listResponse.Content.ReadFromJsonAsync<List<SubscriptionDto>>(JsonOptions);
        Assert.DoesNotContain(list!, s => s.Id == created.Id);
    }

    [Fact]
    public async Task DeleteSubscription_ForAnotherUsersRecord_Returns404()
    {
        var ownerClient = await CreateAuthenticatedClientAsync($"delete-owner-{Guid.NewGuid()}@example.com");
        var attackerClient = await CreateAuthenticatedClientAsync($"delete-attacker-{Guid.NewGuid()}@example.com");

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var deleteResponse = await attackerClient.DeleteAsync($"/api/v1/subscriptions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        // The owner's record must be unaffected by the attacker's failed delete attempt.
        var ownerGetResponse = await ownerClient.GetAsync($"/api/v1/subscriptions/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, ownerGetResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteSubscription_AlreadyDeleted_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync($"delete-twice-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var firstDelete = await client.DeleteAsync($"/api/v1/subscriptions/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        var secondDelete = await client.DeleteAsync($"/api/v1/subscriptions/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task DeleteSubscription_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync($"/api/v1/subscriptions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = "nflx" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_WithEmptyInput_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"resolve-empty-{Guid.NewGuid()}@example.com");

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = "" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_RepeatedCall_EventuallyAutoFillsFromTheEntryTheFirstCallCreated()
    {
        // FakeEmbeddingClient returns the same vector for every input, so once any resolve call
        // (in this test or an earlier one sharing the same test database) has created a catalog
        // row, every subsequent call is an exact (distance 0) match against it.
        var client = await CreateAuthenticatedClientAsync($"resolve-repeat-{Guid.NewGuid()}@example.com");

        var firstResponse = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = "nflx mobile plan" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<ResolveSubscriptionResponse>(JsonOptions);
        Assert.NotNull(first);
        Assert.True(first!.Tier is MatchConfidenceTier.Manual or MatchConfidenceTier.AutoFill);

        var secondResponse = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = "some other free text" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<ResolveSubscriptionResponse>(JsonOptions);
        Assert.NotNull(second);
        Assert.Equal(MatchConfidenceTier.AutoFill, second!.Tier);
        Assert.NotNull(second.CatalogId);
    }

    [Fact]
    public async Task Resolve_WithinRateLimit_Succeeds()
    {
        // Test config caps this endpoint at 3 requests/minute per user (ApiWebApplicationFactory).
        var client = await CreateAuthenticatedClientAsync($"resolve-withinlimit-{Guid.NewGuid()}@example.com");

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = $"input {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Resolve_ExceedingRateLimit_Returns429()
    {
        var client = await CreateAuthenticatedClientAsync($"resolve-exceedlimit-{Guid.NewGuid()}@example.com");

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = $"input {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var fourthResponse = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = "one too many" }, JsonOptions);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourthResponse.StatusCode);
    }

    [Fact]
    public async Task Resolve_RateLimit_IsScopedPerUser_NotGlobal()
    {
        var firstUserClient = await CreateAuthenticatedClientAsync($"resolve-peruser-a-{Guid.NewGuid()}@example.com");
        var secondUserClient = await CreateAuthenticatedClientAsync($"resolve-peruser-b-{Guid.NewGuid()}@example.com");

        for (var i = 0; i < 3; i++)
        {
            var response = await firstUserClient.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = $"input {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // First user is now at their limit; a different authenticated user must be unaffected.
        var secondUserResponse = await secondUserClient.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = "unaffected" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, secondUserResponse.StatusCode);
    }

    [Fact]
    public async Task CreateSubscription_IsNotAffectedByAiResolveRateLimit()
    {
        var client = await CreateAuthenticatedClientAsync($"resolve-scoped-otherendpoint-{Guid.NewGuid()}@example.com");

        for (var i = 0; i < 3; i++)
        {
            var resolveResponse = await client.PostAsJsonAsync("/api/v1/subscriptions/resolve", new ResolveSubscriptionRequest { Input = $"input {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        }

        // The AI-resolve rate limit must not leak onto other endpoints for the same user.
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }

    private async Task SetDefaultAlertDaysAdvanceAsync(string email, int? defaultAlertDaysAdvance)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.SingleAsync(u => u.Email == email);
        user.DefaultAlertDaysAdvance = defaultAlertDaysAdvance;
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateSubscription_WithoutAlertDays_InheritsTheUsersGlobalDefault()
    {
        var email = $"alertdays-global-{Guid.NewGuid()}@example.com";
        var client = await CreateAuthenticatedClientAsync(email);
        await SetDefaultAlertDaysAdvanceAsync(email, 7);

        var request = ValidRequest();
        request.AlertDaysAdvance = null;
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(7, created!.AlertDaysAdvance);
    }

    [Fact]
    public async Task CreateSubscription_WithExplicitAlertDays_OverridesTheGlobalDefault()
    {
        var email = $"alertdays-explicit-{Guid.NewGuid()}@example.com";
        var client = await CreateAuthenticatedClientAsync(email);
        await SetDefaultAlertDaysAdvanceAsync(email, 7);

        var request = ValidRequest();
        request.AlertDaysAdvance = 1;
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(1, created!.AlertDaysAdvance);
    }

    [Fact]
    public async Task CreateSubscription_WithNoStoredPreferenceAndNoAlertDays_FallsBackToThree()
    {
        var client = await CreateAuthenticatedClientAsync($"alertdays-fallback-{Guid.NewGuid()}@example.com");

        var request = ValidRequest();
        request.AlertDaysAdvance = null;
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(3, created!.AlertDaysAdvance);
    }

    [Fact]
    public async Task UpdateSubscription_WithoutAlertDays_LeavesTheStoredLeadTimeUnchanged()
    {
        var email = $"alertdays-update-{Guid.NewGuid()}@example.com";
        var client = await CreateAuthenticatedClientAsync(email);
        var createRequest = ValidRequest();
        createRequest.AlertDaysAdvance = 10;
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", createRequest, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        // The user's global default must not leak into an edit that never mentioned lead time.
        await SetDefaultAlertDaysAdvanceAsync(email, 7);
        var updateRequest = ValidRequest();
        updateRequest.AlertDaysAdvance = null;
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", updateRequest, JsonOptions);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(10, updated!.AlertDaysAdvance);
    }

    [Fact]
    public async Task UpdateSubscription_CanDeactivateAndReactivateWithoutLosingTheRecord()
    {
        var client = await CreateAuthenticatedClientAsync($"deactivate-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var deactivate = ValidRequest();
        deactivate.IsActive = false;
        var deactivated = await (await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", deactivate, JsonOptions))
            .Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.False(deactivated!.IsActive);

        // Omitting the field must preserve the deactivated state, not silently reactivate.
        var untouched = await (await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", ValidRequest(), JsonOptions))
            .Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.False(untouched!.IsActive);

        var reactivate = ValidRequest();
        reactivate.IsActive = true;
        var reactivated = await (await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", reactivate, JsonOptions))
            .Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);
        Assert.True(reactivated!.IsActive);
    }

    [Fact]
    public async Task DeactivatedSubscription_IsExcludedFromBurnRateTotals()
    {
        var client = await CreateAuthenticatedClientAsync($"deactivate-burnrate-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var activeTotal = await client.GetFromJsonAsync<BurnRateResult>("/api/v1/dashboard/burn-rate", JsonOptions);
        Assert.True(activeTotal!.Monthly > 0);

        var deactivate = ValidRequest();
        deactivate.IsActive = false;
        await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", deactivate, JsonOptions);

        var afterTotal = await client.GetFromJsonAsync<BurnRateResult>("/api/v1/dashboard/burn-rate", JsonOptions);
        Assert.Equal(0m, afterTotal!.Monthly);
    }

    private async Task<Guid> CreateCatalogItemAsync(string logoUrl)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var catalogItem = new SubVora.Domain.Entities.SubscriptionCatalogItem
        {
            ProviderName = $"Provider-{Guid.NewGuid()}",
            LogoUrl = logoUrl,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.SubscriptionCatalog.Add(catalogItem);
        await dbContext.SaveChangesAsync();
        return catalogItem.Id;
    }

    [Fact]
    public async Task CreateSubscription_WithCatalogId_PersistsItAndReturnsTheCatalogLogoUrl()
    {
        var client = await CreateAuthenticatedClientAsync($"create-catalog-{Guid.NewGuid()}@example.com");
        var catalogId = await CreateCatalogItemAsync("https://example.com/seeded-logo.svg");

        var request = ValidRequest();
        request.CatalogId = catalogId;
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(catalogId, created!.CatalogId);
        Assert.Equal("https://example.com/seeded-logo.svg", created.CatalogLogoUrl);
    }

    [Fact]
    public async Task CreateSubscription_WithoutCatalogId_SucceedsWithNoLogo()
    {
        var client = await CreateAuthenticatedClientAsync($"create-nocatalog-{Guid.NewGuid()}@example.com");

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await response.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(created!.CatalogId);
        Assert.Null(created.CatalogLogoUrl);
    }

    [Fact]
    public async Task UpdateSubscription_CanAttachAndClearTheCatalogReference()
    {
        var client = await CreateAuthenticatedClientAsync($"update-catalog-{Guid.NewGuid()}@example.com");
        var catalogId = await CreateCatalogItemAsync("https://example.com/attached-logo.svg");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var attachRequest = ValidRequest();
        attachRequest.CatalogId = catalogId;
        var attachResponse = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", attachRequest, JsonOptions);
        var attached = await attachResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, attachResponse.StatusCode);
        Assert.Equal(catalogId, attached!.CatalogId);
        Assert.Equal("https://example.com/attached-logo.svg", attached.CatalogLogoUrl);

        // Omitting the field on a later edit detaches - the user renamed away from the match.
        var detachResponse = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created.Id}", ValidRequest(), JsonOptions);
        var detached = await detachResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, detachResponse.StatusCode);
        Assert.Null(detached!.CatalogId);
        Assert.Null(detached.CatalogLogoUrl);
    }

    [Fact]
    public async Task CreateSubscription_WithUnknownCatalogId_Returns400NotAForeignKey500()
    {
        var client = await CreateAuthenticatedClientAsync($"create-badcatalog-{Guid.NewGuid()}@example.com");

        var request = ValidRequest();
        request.CatalogId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSubscription_WithUnknownCatalogId_Returns400NotAForeignKey500()
    {
        var client = await CreateAuthenticatedClientAsync($"update-badcatalog-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/subscriptions", ValidRequest(), JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<SubscriptionDto>(JsonOptions);

        var request = ValidRequest();
        request.CatalogId = Guid.NewGuid();
        var response = await client.PutAsJsonAsync($"/api/v1/subscriptions/{created!.Id}", request, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
