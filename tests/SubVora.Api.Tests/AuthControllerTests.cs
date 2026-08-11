using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using SubVora.Application.Auth;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

public class AuthControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithValidEmailAndPassword_Returns202AndCreatesAUsableAccount()
    {
        var client = _factory.CreateClient();
        var request = new RegisterRequest { Email = $"register-{Guid.NewGuid()}@example.com", Password = "correct-horse-battery-staple" };

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // The response says nothing, so the account is proven by using it.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = request.Email, Password = request.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Register_WithADuplicateEmail_IsIndistinguishableFromANewOne()
    {
        // The whole point: a caller must not be able to test which addresses have accounts, so a
        // duplicate cannot differ by status, by body, or by anything else on the wire.
        var client = _factory.CreateClient();
        var request = new RegisterRequest { Email = $"duplicate-{Guid.NewGuid()}@example.com", Password = "correct-horse-battery-staple" };

        var first = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        var second = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_WithANewEmail_SendsAWelcomeEmail()
    {
        // End to end through the controller, because the wiring is where this has actually failed:
        // the service enqueues, a hosted service delivers, and nothing in the response says either
        // happened - so only a test that looks at the mailer can tell the difference.
        var client = _factory.CreateClient();
        var email = $"welcome-{Guid.NewGuid()}@example.com";
        var emailSender = _factory.Services.GetRequiredService<FakeEmailSender>();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = Assert.Single(emailSender.SentEmails, e => e.ToEmail == email);
        Assert.Contains("Welcome", sent.Subject);
    }

    [Fact]
    public async Task Register_WithADuplicateEmail_EmailsTheExistingOwnerInstead()
    {
        // Silence towards the caller is only acceptable because the address's real owner is told.
        var client = _factory.CreateClient();
        var email = $"notice-{Guid.NewGuid()}@example.com";
        var emailSender = _factory.Services.GetRequiredService<FakeEmailSender>();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        var beforeSecond = emailSender.SentEmails.Count(e => e.ToEmail == email);

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "a-completely-different-password" });

        Assert.Equal(beforeSecond + 1, emailSender.SentEmails.Count(e => e.ToEmail == email));
    }

    [Fact]
    public async Task Register_WithADuplicateEmail_DoesNotChangeTheExistingPassword()
    {
        // A silent duplicate must not become an account-takeover: the second registration's
        // password has to be worthless against the existing account.
        var client = _factory.CreateClient();
        var email = $"takeover-{Guid.NewGuid()}@example.com";
        const string originalPassword = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = originalPassword });
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "attacker-chosen-password" });

        var attacker = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = "attacker-chosen-password" });
        var owner = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = originalPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, attacker.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessAndRefreshToken()
    {
        var client = _factory.CreateClient();
        var email = $"login-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);

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
        Assert.Equal(HttpStatusCode.Accepted, registerResponse.StatusCode);

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

    [Fact]
    public async Task Register_SameEmailConcurrently_CreatesExactlyOneAccountWithoutA500()
    {
        // The AnyAsync pre-check is a fast path, not a guarantee. Two simultaneous registrations
        // both pass it and race the unique index on users.email; the loser must land on the same
        // silent path as a sequential duplicate rather than surfacing a 500.
        var client = _factory.CreateClient();
        var email = $"register-race-{Guid.NewGuid()}@example.com";
        var request = new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/register", request)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));

        using var scope = _factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbContext.Users.CountAsync(u => u.Email == email));
    }
}
