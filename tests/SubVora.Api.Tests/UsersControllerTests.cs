using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application.Auth;
using SubVora.Application.Users;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

public class UsersControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UsersControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        const string password = "Correct-horse-battery-staple1";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetMe_ReturnsProfile()
    {
        var email = $"users-getme-{Guid.NewGuid()}@example.com";
        var client = await CreateAuthenticatedClientAsync(email);

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(profile);
        Assert.Equal(email, profile!.Email);
        Assert.Equal("INR", profile.PreferredCurrency);
        Assert.Null(profile.DefaultAlertDaysAdvance);
    }

    [Fact]
    public async Task GetMe_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WhenTheRowIsGone_Returns404()
    {
        // The token is still valid - it is signed, not looked up - so the request authenticates and
        // then finds nothing. Answering 200 with a null body made that indistinguishable from a
        // successful read; PUT has always answered 404 here.
        var email = $"users-getme-vanished-{Guid.NewGuid()}@example.com";
        var client = await CreateAuthenticatedClientAsync(email);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
        }

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMe_PersistsChanges()
    {
        var client = await CreateAuthenticatedClientAsync($"users-updateme-{Guid.NewGuid()}@example.com");

        var response = await client.PutAsJsonAsync("/api/v1/users/me", new UpdateUserProfileRequest
        {
            PreferredCurrency = "EUR",
            DefaultAlertDaysAdvance = 5,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(updated);
        Assert.Equal("EUR", updated!.PreferredCurrency);
        Assert.Equal(5, updated.DefaultAlertDaysAdvance);

        var getResponse = await client.GetAsync("/api/v1/users/me");
        var reloaded = await getResponse.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.Equal("EUR", reloaded!.PreferredCurrency);
        Assert.Equal(5, reloaded.DefaultAlertDaysAdvance);
    }

    [Fact]
    public async Task UpdateMe_WithInvalidCurrency_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"users-invalidcurrency-{Guid.NewGuid()}@example.com");

        var response = await client.PutAsJsonAsync("/api/v1/users/me", new UpdateUserProfileRequest
        {
            PreferredCurrency = "XX",
            DefaultAlertDaysAdvance = 5,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- DELETE /users/me (#251) ------------------------------------------------------------

    private const string Password = "Correct-horse-battery-staple1"; // pragma: allowlist secret

    private async Task<(HttpClient Client, AuthTokenResponse Tokens, Guid UserId, string Email)> SignUpWithDataAsync()
    {
        var email = $"users-delete-{Guid.NewGuid()}@example.com";
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = Password });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        var tokens = (await login.Content.ReadFromJsonAsync<AuthTokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        // One of everything a user can own, so the cascade has something to remove.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/subscriptions", NewSubscription())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/categories", new { name = "Mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/payment-sources", new { label = "Visa", sourceType = "Card" })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return (client, tokens, userId, email);
    }

    private static object NewSubscription() => new
    {
        customName = "Netflix",
        costAmount = 299m,
        currency = "INR",
        cycleCadence = "Monthly",
        purchaseDate = "2026-01-01",
        nextBillingDate = "2026-11-01",
    };

    private static Task<HttpResponseMessage> DeleteMeAsync(HttpClient client, string password) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new DeleteAccountRequest { CurrentPassword = password }),
        });

    private async Task<(int Subscriptions, int Categories, int PaymentSources, int Tokens)> OwnedRowsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (
            await db.UserSubscriptions.CountAsync(s => s.UserId == userId),
            await db.Categories.CountAsync(c => c.UserId == userId),
            await db.PaymentSources.CountAsync(p => p.UserId == userId),
            await db.RefreshTokens.CountAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task DeleteMe_WithTheRightPassword_RemovesTheAccountAndEverythingItOwns()
    {
        var (client, tokens, userId, email) = await SignUpWithDataAsync();

        var response = await DeleteMeAsync(client, Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal((0, 0, 0, 0), await OwnedRowsAsync(userId));

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var refresh = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task DeleteMe_WithAWrongPassword_Returns400_AndKeepsEverything()
    {
        var (client, _, userId, _) = await SignUpWithDataAsync();

        var response = await DeleteMeAsync(client, "not-the-password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal((1, 1, 1, 1), await OwnedRowsAsync(userId));
    }

    [Fact]
    public async Task DeleteMe_WithoutAToken_Returns401()
    {
        var response = await DeleteMeAsync(_factory.CreateClient(), Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMe_ThenAWriteWithTheStillValidAccessToken_Returns401NotA500()
    {
        // The access token is signed, not looked up, so it outlives the account by up to 15 minutes.
        // A create in that window hits the users foreign key; that is a signed-out caller.
        var (client, _, _, _) = await SignUpWithDataAsync();
        await DeleteMeAsync(client, Password);

        var response = await client.PostAsJsonAsync("/api/v1/subscriptions", NewSubscription());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMe_LeavesOtherAccountsUntouched()
    {
        var (bystander, _, bystanderId, _) = await SignUpWithDataAsync();
        var (client, _, _, _) = await SignUpWithDataAsync();

        await DeleteMeAsync(client, Password);

        Assert.Equal(HttpStatusCode.OK, (await bystander.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal((1, 1, 1, 1), await OwnedRowsAsync(bystanderId));
    }
}
