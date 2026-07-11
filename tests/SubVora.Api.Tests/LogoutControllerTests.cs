using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SubVora.Application.Auth;

namespace SubVora.Api.Tests;

public class LogoutControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public LogoutControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<AuthTokenResponse> RegisterAndLoginAsync(HttpClient client, string email, string password = "correct-horse-battery-staple")
    {
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(tokens);
        return tokens!;
    }

    [Fact]
    public async Task Logout_WithValidRefreshToken_RevokesIt()
    {
        var client = _factory.CreateClient();
        var email = $"logout-{Guid.NewGuid()}@example.com";
        var tokens = await RegisterAndLoginAsync(client, email);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_ThenAttemptRefresh_Returns401()
    {
        var client = _factory.CreateClient();
        var email = $"logout-refresh-{Guid.NewGuid()}@example.com";
        var tokens = await RegisterAndLoginAsync(client, email);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutAccessToken_Returns401()
    {
        var client = _factory.CreateClient();
        var email = $"logout-noauth-{Guid.NewGuid()}@example.com";
        var tokens = await RegisterAndLoginAsync(client, email);

        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_WithAnotherUsersRefreshToken_DoesNotRevokeIt()
    {
        var client = _factory.CreateClient();
        var ownerTokens = await RegisterAndLoginAsync(client, $"owner-{Guid.NewGuid()}@example.com");
        var attackerTokens = await RegisterAndLoginAsync(client, $"attacker-{Guid.NewGuid()}@example.com");

        // Attacker is authenticated as themselves but tries to log out someone else's token.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attackerTokens.AccessToken);
        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest { RefreshToken = ownerTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        // The owner's token must still work - logout is scoped to the caller's own tokens.
        client.DefaultRequestHeaders.Authorization = null;
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = ownerTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
    }
}
