using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SubVora.Application.Auth;

namespace SubVora.Api.Tests;

public class AuthRateLimitTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthRateLimitTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_ExceedingPermitLimit_Returns429()
    {
        // The shared factory's default is high (so it doesn't trip other test classes' setup
        // helpers) - override it down to a small value scoped to just this client/host instance.
        var scopedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Auth:PermitLimit"] = "3",
                ["RateLimiting:Auth:WindowSeconds"] = "60",
            })));

        var client = scopedFactory.CreateClient();
        var request = new LoginRequest { Email = "no-such-user@example.com", Password = "wrong-password" };

        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 3; i++)
        {
            lastResponse = await client.PostAsJsonAsync("/api/v1/auth/login", request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
        }

        lastResponse = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_IsNotRateLimitedByTheAuthPolicy()
    {
        // Logout requires [Authorize], not [EnableRateLimiting("auth")] - a handful of
        // unauthenticated calls should all come back 401, never 429.
        var client = _factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest { RefreshToken = "irrelevant" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    private WebApplicationFactory<Program> WithLimits(int authPermitLimit, int refreshPermitLimit = 1000) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Auth:PermitLimit"] = authPermitLimit.ToString(),
                ["RateLimiting:Auth:WindowSeconds"] = "60",
                ["RateLimiting:Refresh:PermitLimit"] = refreshPermitLimit.ToString(),
                ["RateLimiting:Refresh:WindowSeconds"] = "60",
            })));

    [Fact]
    public async Task Refresh_DoesNotShareTheLoginBucket()
    {
        // Refresh runs on a timer in every signed-in client, and a carrier NAT puts many of them
        // behind one IP. A few logins from that IP must not 429 everyone's refresh.
        var client = WithLimits(authPermitLimit: 3).CreateClient();
        var email = $"rate-refresh-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" }); // pragma: allowlist secret
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = "correct-horse-battery-staple" }); // pragma: allowlist secret
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();

        await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = "wrong-password" });
        var exhausted = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = "wrong-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = tokens!.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task Refresh_HasItsOwnLimit()
    {
        var client = WithLimits(authPermitLimit: 1000, refreshPermitLimit: 2).CreateClient();
        var request = new RefreshRequest { RefreshToken = "not-a-real-token" };

        await client.PostAsJsonAsync("/api/v1/auth/refresh", request);
        await client.PostAsJsonAsync("/api/v1/auth/refresh", request);
        var third = await client.PostAsJsonAsync("/api/v1/auth/refresh", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task RateLimitRejection_CarriesRetryAfter()
    {
        var client = WithLimits(authPermitLimit: 1).CreateClient();
        var request = new LoginRequest { Email = "no-such-user@example.com", Password = "wrong-password" };

        await client.PostAsJsonAsync("/api/v1/auth/login", request);
        var rejected = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var retryAfter = Assert.Single(rejected.Headers.GetValues("Retry-After"));
        Assert.True(int.TryParse(retryAfter, out var seconds) && seconds > 0, $"Retry-After was {retryAfter}.");
    }
}
