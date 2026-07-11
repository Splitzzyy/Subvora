using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application.Auth;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

public class RefreshTokenControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RefreshTokenControllerTests(ApiWebApplicationFactory factory)
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
    public async Task Refresh_WithValidToken_ReturnsNewTokenPairAndRevokesOldToken()
    {
        var client = _factory.CreateClient();
        var email = $"refresh-valid-{Guid.NewGuid()}@example.com";
        var originalTokens = await RegisterAndLoginAsync(client, email);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = originalTokens.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var newTokens = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(newTokens);
        Assert.NotEqual(originalTokens.RefreshToken, newTokens!.RefreshToken);
        Assert.NotEqual(originalTokens.AccessToken, newTokens.AccessToken);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var originalHash = jwtTokenService.HashRefreshToken(originalTokens.RefreshToken);
        var storedOriginal = await dbContext.RefreshTokens.SingleAsync(t => t.TokenHash == originalHash);
        Assert.NotNull(storedOriginal.RevokedAt);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        var client = _factory.CreateClient();
        var email = $"refresh-expired-{Guid.NewGuid()}@example.com";
        var tokens = await RegisterAndLoginAsync(client, email);

        // Directly backdate the just-issued token's expiry, since login always issues a
        // fresh 30-day token and there is no product flow that produces an expired one.
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var hash = jwtTokenService.HashRefreshToken(tokens.RefreshToken);
            var storedToken = await dbContext.RefreshTokens.SingleAsync(t => t.TokenHash == hash);
            storedToken.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithAlreadyRotatedToken_RevokesEntireChainAndReturns401()
    {
        var client = _factory.CreateClient();
        var email = $"refresh-reuse-{Guid.NewGuid()}@example.com";
        var firstTokens = await RegisterAndLoginAsync(client, email);

        var rotateResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = firstTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var secondTokens = await rotateResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(secondTokens);

        // Reuse the now-stale (already rotated) first refresh token.
        var reuseResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = firstTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // The legitimately-rotated second token must also be revoked as a result of the
        // reuse-detection chain revocation, even though it was never itself reused.
        var secondTokenNowRevokedResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = secondTokens!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, secondTokenNowRevokedResponse.StatusCode);
    }
}
