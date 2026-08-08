using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SubVora.Application.Auth;
using SubVora.Application.Notifications;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Auth;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Tests;

public class AuthServiceTests : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private readonly CountingPasswordHasher _passwordHasher = new();
    private readonly RecordingEmailSender _emailSender = new();
    private AppDbContext _dbContext = null!;
    private AuthService _authService = null!;

    public AuthServiceTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _dbContext = new AppDbContext(AppDbContextOptionsFactory.Build(_fixture.ConnectionString));
        await _dbContext.Database.MigrateAsync();
        _authService = new AuthService(_dbContext, _passwordHasher, new StubJwtTokenService(), _emailSender, NullLogger<AuthService>.Instance);
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task LoginAsync_WithAnUnknownEmail_StillVerifiesAPasswordHash()
    {
        // The timing leak this closes: BCrypt at work factor 12 costs ~250ms, so skipping it for a
        // missing account made "no such user" measurably faster than "wrong password" and turned
        // login into an enumeration oracle. The dummy verify is what equalises the two.
        var result = await _authService.LoginAsync(new LoginRequest
        {
            Email = $"nobody-{Guid.NewGuid()}@example.com",
            Password = "correct-horse-battery-staple",
        });

        Assert.False(result.Succeeded);
        Assert.Equal(1, _passwordHasher.VerifyCalls);
    }

    [Fact]
    public async Task LoginAsync_WithAKnownEmailAndWrongPassword_VerifiesExactlyOnceToo()
    {
        // Same call count on both paths, or the work itself becomes the signal.
        var email = $"known-{Guid.NewGuid()}@example.com";
        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        _passwordHasher.VerifyCalls = 0;

        var result = await _authService.LoginAsync(new LoginRequest { Email = email, Password = "not-the-password" });

        Assert.False(result.Succeeded);
        Assert.Equal(1, _passwordHasher.VerifyCalls);
    }

    [Fact]
    public async Task RegisterAsync_WithANewEmail_SendsNoEmail()
    {
        var email = $"fresh-{Guid.NewGuid()}@example.com";

        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });

        Assert.DoesNotContain(_emailSender.Sent, e => e.To == email);
        Assert.True(await _dbContext.Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task RegisterAsync_WithADuplicateEmail_NotifiesTheOwnerAndLeavesTheAccountAlone()
    {
        var email = $"dupe-{Guid.NewGuid()}@example.com";
        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        var originalHash = (await _dbContext.Users.AsNoTracking().SingleAsync(u => u.Email == email)).PasswordHash;

        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "attacker-chosen-password" });

        Assert.Single(_emailSender.Sent, e => e.To == email);
        Assert.Equal(1, await _dbContext.Users.CountAsync(u => u.Email == email));
        Assert.Equal(originalHash, (await _dbContext.Users.AsNoTracking().SingleAsync(u => u.Email == email)).PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_WhenTheNoticeEmailFails_StillReturnsQuietly()
    {
        // A duplicate has to be indistinguishable from a new registration, so a mail outage cannot
        // be allowed to turn into a 500 that gives the answer away.
        var email = $"mail-down-{Guid.NewGuid()}@example.com";
        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        _emailSender.ThrowOnSend = true;

        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
    }

    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        public int VerifyCalls { get; set; }

        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string hash)
        {
            VerifyCalls++;
            return hash == $"hashed:{password}";
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public bool ThrowOnSend { get; set; }

        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
            {
                throw new InvalidOperationException("SMTP is down");
            }

            Sent.Add((toEmail, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        public GeneratedAccessToken GenerateAccessToken(Guid userId, string email) =>
            new($"access-{userId}", DateTimeOffset.UtcNow.AddMinutes(15));

        public GeneratedRefreshToken GenerateRefreshToken()
        {
            var plain = Guid.NewGuid().ToString("N");
            return new GeneratedRefreshToken(plain, HashRefreshToken(plain), DateTimeOffset.UtcNow.AddDays(30));
        }

        public string HashRefreshToken(string plainToken) => $"hash:{plainToken}";
    }
}
