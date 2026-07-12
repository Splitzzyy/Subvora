using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SubVora.Application.Matching;
using SubVora.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SubVora.Api.Tests;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Test-only signing key, never used outside this ephemeral Testcontainers-backed run.
    public const string TestJwtSecret = "test-only-jwt-signing-secret-never-used-outside-ci-32bytes-min";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("subvora_api_test")
        .WithUsername("subvora_api_test")
        .WithPassword("subvora_api_test_password")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:Secret"] = TestJwtSecret,
                ["Jwt:Issuer"] = "SubVora.Tests",
                ["Jwt:Audience"] = "SubVora.Tests",
                // Never a real key: IEmbeddingClient is swapped for a fake below, so nothing ever
                // dials out to OpenAI in tests, but the typed HttpClient factory still requires a
                // non-null value at registration time.
                ["OpenAI:ApiKey"] = "test-only-openai-key-never-used-in-ci",
                // Small on purpose: lets rate-limit tests exceed the window with a handful of
                // requests instead of the production default (30/min).
                ["RateLimiting:AiResolve:PermitLimit"] = "3",
                ["RateLimiting:AiResolve:WindowSeconds"] = "60",
                // High on purpose: register/login are used as setup by nearly every controller
                // test class (via a shared per-class factory + rate limiter instance), so this
                // must not trip during normal test runs. AuthRateLimitTests overrides this down
                // to a small value on its own WithWebHostBuilder-derived factory instead.
                ["RateLimiting:Auth:PermitLimit"] = "1000",
                ["RateLimiting:Auth:WindowSeconds"] = "60",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmbeddingClient>();
            services.AddScoped<IEmbeddingClient, FakeEmbeddingClient>();
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
