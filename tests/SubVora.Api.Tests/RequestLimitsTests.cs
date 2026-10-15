using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SubVora.Application.Auth;

namespace SubVora.Api.Tests;

/// <summary>Limits that apply to every request, not to one endpoint: the body-size cap and the global rate limiter.</summary>
public class RequestLimitsTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Password = "correct-horse-battery-staple"; // pragma: allowlist secret

    private readonly ApiWebApplicationFactory _factory;

    public RequestLimitsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> WithGlobalLimit(int perUser = 100_000, int anonymous = 100_000) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Global:PermitLimit"] = perUser.ToString(),
                ["RateLimiting:Global:AnonymousPermitLimit"] = anonymous.ToString(),
            })));

    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var email = $"limits-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = Password });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task OversizedBody_Returns413()
    {
        // ~100 KB of otherwise valid JSON, anonymously, to an endpoint anyone can reach. The bulk is
        // in a property the endpoint ignores, so only the size can be what rejects it.
        var client = _factory.CreateClient();
        var json = $$"""{"email":"nobody@example.com","password":"x","padding":"{{new string('a', 100 * 1024)}}"}""";

        var response = await client.PostAsync("/api/v1/auth/login", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task NormalBody_IsUnaffected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = "nobody@example.com", Password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GlobalLimit_AppliesToAuthenticatedEndpoints()
    {
        // Register and login spend the IP's tokens, not the user's - the user bucket starts full.
        var client = await SignedInClientAsync(WithGlobalLimit(perUser: 3));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/subscriptions")).StatusCode);
        }

        var limited = await client.GetAsync("/api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    [Fact]
    public async Task GlobalLimit_IsPerUser()
    {
        var factory = WithGlobalLimit(perUser: 4);
        var first = await SignedInClientAsync(factory);
        var second = await SignedInClientAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await first.GetAsync("/api/v1/subscriptions");
        }

        var response = await second.GetAsync("/api/v1/subscriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GlobalLimit_AppliesToHealthReady()
    {
        // Each hit opens a Postgres connection, and polled anonymously it kept Neon's compute awake.
        // Anonymous, so the per-address budget applies, not the per-user one.
        var client = WithGlobalLimit(perUser: 100_000, anonymous: 3).CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task HealthLive_IsExemptEvenWhenTheAddressIsDrained()
    {
        // Render's health check and the keep-warm ping hit it; a 429 would read as an unhealthy
        // instance and get it restarted.
        var client = WithGlobalLimit(anonymous: 1).CreateClient();
        await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/health/ready")).StatusCode);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousTraffic_HasItsOwnBudget()
    {
        // Carrier NAT puts many phones behind one address; the per-user limit must not be what
        // caps them.
        var client = WithGlobalLimit(perUser: 2, anonymous: 100_000).CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        }
    }
}
