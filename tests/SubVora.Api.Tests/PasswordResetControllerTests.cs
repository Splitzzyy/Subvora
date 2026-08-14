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
        var before = ResetCodesSentTo(email);
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest { Email = email });

        // FakeEmailSender.SentEmails is a ConcurrentBag, which has no insertion order - Last() here
        // returned the *oldest* code, so asking twice handed back the same one. The address can now
        // also receive an already-registered notice, so select the new six-digit code by content.
        return Assert.Single(ResetCodesSentTo(email).Except(before));
    }

    private IReadOnlyCollection<string> ResetCodesSentTo(string email) =>
        _factory.Services.GetRequiredService<FakeEmailSender>().SentEmails
            .Where(e => e.ToEmail == email)
            .Select(e => Regex.Match(e.Body, @"\b(\d{6})\b").Groups[1].Value)
            .Where(code => code.Length == 6)
            .ToList();

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
    public async Task ForgotPassword_UnknownEmail_WritesNoResetCodeRow()
    {
        // Both branches now generate and hash a code before the user lookup, so response time
        // depends less on whether the address exists. The unknown branch must still persist
        // nothing - the hashing is throwaway work, not a row.
        //
        // Timing parity itself is held by the structure of ForgotPasswordAsync (and its comment
        // recording the accepted residual gap), not asserted here: a wall-clock assertion would be
        // flaky in CI and would fail for reasons unrelated to the property it claims to check.
        var client = _factory.CreateClient();
        var unknownEmail = $"unknown-norow-{Guid.NewGuid()}@example.com";

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await dbContext.PasswordResetCodes.CountAsync();

        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest { Email = unknownEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await dbContext.PasswordResetCodes.CountAsync());
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
    public async Task ResetPassword_ValidCode_RevokesEverySessionOpenBeforeTheReset()
    {
        var client = _factory.CreateClient();
        var email = $"reset-revokes-{Guid.NewGuid()}@example.com";
        const string oldPassword = "correct-horse-battery-staple";
        const string newPassword = "new-correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = oldPassword });
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = oldPassword });
        var priorSession = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(priorSession);

        var code = await RequestResetCodeAsync(client, email);
        var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = code, NewPassword = newPassword });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // The refresh token that session still holds must be dead - otherwise a reset only blocks
        // the old password while whoever holds the token keeps minting access tokens for 30 days.
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = priorSession!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

        // ...and the account is still usable with the new password.
        var newLoginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = newPassword });
        Assert.Equal(HttpStatusCode.OK, newLoginResponse.StatusCode);
        var newSession = await newLoginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        var newRefreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = newSession!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, newRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_FailedAttempt_LeavesExistingSessionsIntact()
    {
        var client = _factory.CreateClient();
        var email = $"reset-failed-keeps-session-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var session = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        await RequestResetCodeAsync(client, email);

        var wrongCodeResponse = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest { Email = email, Code = "000000", NewPassword = "new-password-123" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCodeResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = session!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
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

    [Fact]
    public async Task ForgotPassword_RequestedTwice_RetiresTheEarlierCode()
    {
        // AttemptCount lives on each code row, so leaving old codes live meant every new request
        // handed out another five guesses at the same six-digit space - "5 attempts" only ever
        // bounded one code.
        var client = _factory.CreateClient();
        var email = $"reset-supersede-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });

        var firstCode = await RequestResetCodeAsync(client, email);
        var secondCode = await RequestResetCodeAsync(client, email);
        Assert.NotEqual(firstCode, secondCode);

        var withOldCode = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest { Email = email, Code = firstCode, NewPassword = "old-code-should-not-work" });

        Assert.Equal(HttpStatusCode.BadRequest, withOldCode.StatusCode);

        var withNewCode = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest { Email = email, Code = secondCode, NewPassword = "new-code-should-work" });

        Assert.Equal(HttpStatusCode.OK, withNewCode.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_RequestedTwice_LeavesOnlyOneLiveCode()
    {
        var client = _factory.CreateClient();
        var email = $"reset-live-count-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });

        await RequestResetCodeAsync(client, email);
        await RequestResetCodeAsync(client, email);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var liveCodes = await dbContext.PasswordResetCodes.AsNoTracking()
            .CountAsync(c => c.UserId == user.Id && c.UsedAt == null);

        Assert.Equal(1, liveCodes);
    }
}
