using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Application.Auth;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

public class PasswordResetControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public PasswordResetControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> RequestResetCodeAsync(HttpClient client, string email)
    {
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest { Email = email });

        var emailSender = _factory.Services.GetRequiredService<FakeEmailSender>();
        var sent = emailSender.SentEmails.Last(e => e.ToEmail == email);
        return Regex.Match(sent.Body, @"\b(\d{6})\b").Groups[1].Value;
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturns200()
    {
        var client = _factory.CreateClient();
        var unknownEmail = $"unknown-{Guid.NewGuid()}@example.com";

        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest { Email = unknownEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var emailSender = _factory.Services.GetRequiredService<FakeEmailSender>();
        Assert.DoesNotContain(emailSender.SentEmails, e => e.ToEmail == unknownEmail);
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_CreatesCodeAndSendsEmail()
    {
        var client = _factory.CreateClient();
        var email = $"forgot-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });

        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest { Email = email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var emailSender = _factory.Services.GetRequiredService<FakeEmailSender>();
        Assert.Contains(emailSender.SentEmails, e => e.ToEmail == email);
    }

    [Fact]
    public async Task ResetPassword_ValidCode_UpdatesPassword()
    {
        var client = _factory.CreateClient();
        var email = $"reset-valid-{Guid.NewGuid()}@example.com";
        const string oldPassword = "correct-horse-battery-staple";
        const string newPassword = "new-correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = oldPassword });
        var code = await RequestResetCodeAsync(client, email);

        var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = code, NewPassword = newPassword });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = newPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ExpiredCode_Returns400()
    {
        var client = _factory.CreateClient();
        var email = $"reset-expired-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var code = await RequestResetCodeAsync(client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await dbContext.Users.SingleAsync(u => u.Email == email.ToLowerInvariant());
            var resetCode = await dbContext.PasswordResetCodes.SingleAsync(c => c.UserId == user.Id);
            resetCode.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = code, NewPassword = "new-password-123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WrongCode_IncrementsAttemptCount()
    {
        var client = _factory.CreateClient();
        var email = $"reset-wrong-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        await RequestResetCodeAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = "000000", NewPassword = "new-password-123" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.SingleAsync(u => u.Email == email.ToLowerInvariant());
        var resetCode = await dbContext.PasswordResetCodes.SingleAsync(c => c.UserId == user.Id);
        Assert.Equal(1, resetCode.AttemptCount);
    }

    [Fact]
    public async Task ResetPassword_TooManyAttempts_InvalidatesCode()
    {
        var client = _factory.CreateClient();
        var email = $"reset-lockout-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var code = await RequestResetCodeAsync(client, email);

        for (var i = 0; i < 5; i++)
        {
            var wrongAttempt = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = "000000", NewPassword = "new-password-123" });
            Assert.Equal(HttpStatusCode.BadRequest, wrongAttempt.StatusCode);
        }

        // Even the correct code is rejected now - the code is locked out after 5 failed attempts.
        var finalAttempt = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = code, NewPassword = "new-password-123" });
        Assert.Equal(HttpStatusCode.BadRequest, finalAttempt.StatusCode);
    }
}
