using System.Net;
using System.Text.Json;

namespace SubVora.Api.Tests;

/// <summary>
/// The generated document is what drives Swagger UI's Authorize button and its per-endpoint
/// padlocks, so these assert the document, not the runtime 401 behavior (covered elsewhere).
/// </summary>
public class OpenApiSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public OpenApiSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<JsonElement> GetDocumentAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Document_DeclaresBearerSecurityScheme()
    {
        var scheme = (await GetDocumentAsync())
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [Theory]
    [InlineData("/api/v1/subscriptions", "get")]
    [InlineData("/api/v1/subscriptions", "post")]
    [InlineData("/api/v1/categories", "get")]
    [InlineData("/api/v1/payment-sources", "get")]
    [InlineData("/api/v1/dashboard/burn-rate", "get")]
    [InlineData("/api/v1/users/me", "get")]
    [InlineData("/api/v1/auth/logout", "post")]
    public async Task SecuredOperations_RequireBearer(string path, string method)
    {
        var operation = (await GetDocumentAsync()).GetProperty("paths").GetProperty(path).GetProperty(method);

        Assert.Contains(
            operation.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("Bearer", out _));
    }

    [Theory]
    [InlineData("/api/v1/auth/register")]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/forgot-password")]
    [InlineData("/api/v1/auth/reset-password")]
    public async Task AnonymousAuthOperations_DeclareNoSecurity(string path)
    {
        var operation = (await GetDocumentAsync()).GetProperty("paths").GetProperty(path).GetProperty("post");

        Assert.False(operation.TryGetProperty("security", out _));
    }
}
