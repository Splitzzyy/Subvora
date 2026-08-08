using System.Net;

namespace SubVora.Api.Tests;

public class HealthCheckTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public HealthCheckTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_WithDatabaseUp_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task Health_RequiresNoAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task AllThreeHealthEndpoints_AreServedAndUnauthenticated(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthLive_RunsNoDatabaseProbe()
    {
        // The point of the split: Render polls /health/live continuously, and a check that opens a
        // Postgres connection holds Neon's scale-to-zero compute awake around the clock. Pointed at
        // a database that cannot be reached, liveness must still answer Healthy - readiness must
        // not.
        await using var brokenDbFactory = _factory.WithUnreachableDatabase();
        var client = brokenDbFactory.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}
