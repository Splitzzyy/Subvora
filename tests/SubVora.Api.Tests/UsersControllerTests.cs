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
}
