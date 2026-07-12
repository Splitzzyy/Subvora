using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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
}
