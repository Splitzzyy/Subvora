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

    private static async Task<AuthTokenResponse> RegisterAndLoginAsync(HttpClient client, string email, string password = "Correct-horse-battery-staple1")
    {
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(tokens);
        return tokens!;
    }

    private static async Task<AuthTokenResponse> LoginAsync(HttpClient client, string email, string password = "Correct-horse-battery-staple1")
    {
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(tokens);
        return tokens!;
    }

    /// <summary>Ages a refresh token out directly - waiting 30 days is not an option.</summary>
    private async Task ExpireRefreshTokenAsync(string plainRefreshToken)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hash = jwtTokenService.HashRefreshToken(plainRefreshToken);

        await dbContext.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    /// <summary>
    /// Moves a rotated token's revocation back past the two-minute reuse grace, so a replay of it
    /// is judged as theft rather than as a client retrying after a lost response.
    /// </summary>
    private async Task AgeRotationPastGraceAsync(string plainRefreshToken)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwtTokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var hash = jwtTokenService.HashRefreshToken(plainRefreshToken);

        await dbContext.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, t => t.RevokedAt - TimeSpan.FromMinutes(3)));
    }

    private async Task<int> LiveRefreshTokenCountAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await dbContext.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await dbContext.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null);
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

        // Reuse the now-stale (already rotated) first refresh token - after the reuse grace, which
        // would otherwise read an immediate replay as a client that lost the response.
        await AgeRotationPastGraceAsync(firstTokens.RefreshToken);
        var reuseResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = firstTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // The legitimately-rotated second token must also be revoked as a result of the
        // reuse-detection chain revocation, even though it was never itself reused.
        var secondTokenNowRevokedResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = secondTokens!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, secondTokenNowRevokedResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_SameTokenTwiceConcurrently_LeavesAtMostOneLiveSession()
    {
        // Rotation used to read RevokedAt and set it in two steps, so two callers presenting the
        // same live token could both see null and both mint a pair - the exact concurrent-theft
        // case rotation exists to catch. The compare-and-swap is what makes the database pick one.
        //
        // Since the reuse grace a second caller may also get a 200 (it cannot be told apart from a
        // client that lost the first response), but only by discarding the pair the first one got:
        // the invariant is one live chain, not one 200.
        var client = _factory.CreateClient();
        var email = $"refresh-race-{Guid.NewGuid()}@example.com";
        var tokens = await RegisterAndLoginAsync(client, email);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken })));

        Assert.True(await LiveRefreshTokenCountAsync(email) <= 1);
    }

    [Fact]
    public async Task Refresh_AfterAConcurrentRotation_LeavesThePresentedTokenDead()
    {
        // Whoever loses the race treats it as reuse and revokes the chain, so the token that was
        // presented twice is unusable afterwards no matter who won.
        var client = _factory.CreateClient();
        var tokens = await RegisterAndLoginAsync(client, $"refresh-race-dead-{Guid.NewGuid()}@example.com");

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken })));
        await AgeRotationPastGraceAsync(tokens.RefreshToken);

        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithAnExpiredTokensChain_DoesNotRevokeTheUsersOtherSessions()
    {
        // Expiry is not theft. A token that simply aged out must fail on its own without taking
        // the user's other devices down with it.
        var client = _factory.CreateClient();
        var email = $"refresh-expiry-{Guid.NewGuid()}@example.com";
        var phoneSession = await RegisterAndLoginAsync(client, email);
        var tabletSession = await LoginAsync(client, email);

        await ExpireRefreshTokenAsync(tabletSession.RefreshToken);

        var expired = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tabletSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        var stillValid = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = phoneSession.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayedRightAfterRotation_IsTreatedAsALostResponse()
    {
        // The phone timed out waiting (a cold start outlasts its refresh timeout) and retries with
        // the token the server already rotated. It must get a pair, not a sign-out everywhere.
        var client = _factory.CreateClient();
        var email = $"refresh-lost-response-{Guid.NewGuid()}@example.com";
        var tokens = await RegisterAndLoginAsync(client, email);
        var lost = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, lost.StatusCode);

        var retried = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(1, await LiveRefreshTokenCountAsync(email));
    }
}
