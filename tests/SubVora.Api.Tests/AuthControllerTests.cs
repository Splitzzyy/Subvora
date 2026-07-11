using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SubVora.Application.Auth;

namespace SubVora.Api.Tests;

public class AuthControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithValidEmailAndPassword_Returns201AndCreatesUser()
    {
        var client = _factory.CreateClient();
        var request = new RegisterRequest { Email = $"register-{Guid.NewGuid()}@example.com", Password = "correct-horse-battery-staple" };

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegisteredUserResponse>();
        Assert.NotNull(body);
        Assert.Equal(request.Email, body!.Email);
        Assert.NotEqual(Guid.Empty, body.Id);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        var request = new RegisterRequest { Email = $"duplicate-{Guid.NewGuid()}@example.com", Password = "correct-horse-battery-staple" };

        var first = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessAndRefreshToken()
    {
        var client = _factory.CreateClient();
        var email = $"login-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(tokens.RefreshTokenExpiresAt > tokens.AccessTokenExpiresAt);

        // Equivalent of the issue's "manual curl smoke test": decode the access token with
        // the configured signing secret and confirm it validates and carries the right claims.
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(tokens.AccessToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "SubVora.Tests",
            ValidateAudience = true,
            ValidAudience = "SubVora.Tests",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiWebApplicationFactory.TestJwtSecret)),
            ValidateLifetime = true,
        }, out _);
        // JwtSecurityTokenHandler's default inbound claim map remaps the short "email"
        // JWT claim name to the long ClaimTypes.Email URI on the resulting principal.
        Assert.Equal(email, principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var email = $"wrongpass-{Guid.NewGuid()}@example.com";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithNonexistentEmail_Returns401()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = $"nobody-{Guid.NewGuid()}@example.com", Password = "whatever-123" });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvalidEmail_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = "not-an-email", Password = "correct-horse-battery-staple" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
