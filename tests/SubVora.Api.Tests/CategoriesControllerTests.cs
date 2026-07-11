using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SubVora.Application.Auth;
using SubVora.Application.Categories;

namespace SubVora.Api.Tests;

public class CategoriesControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CategoriesControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        const string password = "correct-horse-battery-staple";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetCategories_ReturnsSystemDefaultsAndCallersOwn()
    {
        var client = await CreateAuthenticatedClientAsync($"cat-list-{Guid.NewGuid()}@example.com");
        var ownName = $"MyCategory-{Guid.NewGuid()}";

        var createResponse = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = ownName });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/categories");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var categories = await listResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();

        Assert.NotNull(categories);
        // Seeded system defaults from the migration (technical_requirements.md §5 placeholder list).
        Assert.Contains(categories!, c => c.Name == "Entertainment" && c.IsSystemDefault);
        Assert.Contains(categories!, c => c.Name == ownName && !c.IsSystemDefault);
    }

    [Fact]
    public async Task GetCategories_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_AddsUserOwnedCategory()
    {
        var client = await CreateAuthenticatedClientAsync($"cat-create-{Guid.NewGuid()}@example.com");
        var name = $"Streaming-{Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(dto);
        Assert.Equal(name, dto!.Name);
        Assert.False(dto.IsSystemDefault);
        Assert.NotEqual(Guid.Empty, dto.Id);
    }

    [Fact]
    public async Task CreateCategory_WithDuplicateNameForUser_Returns409()
    {
        var client = await CreateAuthenticatedClientAsync($"cat-dup-{Guid.NewGuid()}@example.com");
        var name = $"Utilities-{Guid.NewGuid()}";

        var first = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_SameNameForDifferentUsers_BothSucceed()
    {
        var name = $"Fitness-{Guid.NewGuid()}";
        var clientA = await CreateAuthenticatedClientAsync($"cat-userA-{Guid.NewGuid()}@example.com");
        var clientB = await CreateAuthenticatedClientAsync($"cat-userB-{Guid.NewGuid()}@example.com");

        var responseA = await clientA.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name });
        var responseB = await clientB.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = name });

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_WithEmptyName_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"cat-empty-{Guid.NewGuid()}@example.com");

        var response = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
