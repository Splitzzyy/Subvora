using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
    public async Task RegisterAsync_WithANewEmail_WelcomesTheNewAccount()
    {
        var email = $"fresh-{Guid.NewGuid()}@example.com";

        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });

        var sent = Assert.Single(_emailSender.Sent, e => e.To == email);
        Assert.Contains("Welcome", sent.Subject);
        Assert.True(await _dbContext.Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task RegisterAsync_WithADuplicateEmail_NotifiesTheOwnerAndLeavesTheAccountAlone()
    {
        var email = $"dupe-{Guid.NewGuid()}@example.com";
        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        var originalHash = (await _dbContext.Users.AsNoTracking().SingleAsync(u => u.Email == email)).PasswordHash;

        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "attacker-chosen-password" });

        var notice = Assert.Single(_emailSender.Sent, e => e.Subject.Contains("Someone tried"));
        Assert.Equal(email, notice.To);
        Assert.Equal(1, await _dbContext.Users.CountAsync(u => u.Email == email));
        Assert.Equal(originalHash, (await _dbContext.Users.AsNoTracking().SingleAsync(u => u.Email == email)).PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_SendsExactlyOneEmail_WhicheverBranchRan()
    {
        // The anti-enumeration property extended to the mailbox: a new account and a duplicate both
        // produce one message, so an observer counting mail cannot tell which happened either.
        var newEmail = $"count-new-{Guid.NewGuid()}@example.com";
        var takenEmail = $"count-dupe-{Guid.NewGuid()}@example.com";
        await _authService.RegisterAsync(new RegisterRequest { Email = takenEmail, Password = "correct-horse-battery-staple" });
        _emailSender.Sent.Clear();

        await _authService.RegisterAsync(new RegisterRequest { Email = newEmail, Password = "correct-horse-battery-staple" });
        await _authService.RegisterAsync(new RegisterRequest { Email = takenEmail, Password = "attacker-chosen-password" });

        Assert.Single(_emailSender.Sent, e => e.To == newEmail);
        Assert.Single(_emailSender.Sent, e => e.To == takenEmail);
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

    [Fact]
    public async Task RegisterAsync_WhenTheWelcomeEmailFails_StillCreatesTheAccount()
    {
        // The mailer is best-effort on both branches. Losing the welcome note is a nuisance; losing
        // the account the user just signed up for is not.
        var email = $"welcome-down-{Guid.NewGuid()}@example.com";
        _emailSender.ThrowOnSend = true;

        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });

        Assert.True(await _dbContext.Users.AnyAsync(u => u.Email == email));
    }

    // ---- Refresh reuse grace (#236) -------------------------------------------------------

    [Fact]
    public async Task Refresh_ReplayWithinGrace_IssuesNewPair_AndRevokesTheLostSuccessor()
    {
        // The lost-response case: the server rotated T1 into T2, the phone never saw T2 and
        // retries with T1. That is a retry, not a thief - it must not sign the user out.
        var (_, first) = await RegisterAndLoginAsync("grace-replay");
        var lost = await RefreshAsync(first.RefreshToken);

        var retried = await _authService.RefreshAsync(first.RefreshToken);

        Assert.True(retried.Succeeded);
        Assert.NotNull(await RevokedAtAsync(lost.RefreshToken));
        Assert.True((await _authService.RefreshAsync(retried.Tokens!.RefreshToken)).Succeeded);
    }

    [Fact]
    public async Task Refresh_ReplayWithinGrace_Twice_RecoversBothTimes()
    {
        // Two lost responses in a row (a cold start, then a dropped connection) are still one
        // client retrying; each replay discards the pair the previous one minted.
        var (userId, first) = await RegisterAndLoginAsync("grace-twice");
        await RefreshAsync(first.RefreshToken);

        Assert.True((await _authService.RefreshAsync(first.RefreshToken)).Succeeded);
        var third = await _authService.RefreshAsync(first.RefreshToken);

        Assert.True(third.Succeeded);
        Assert.Equal(1, await LiveTokenCountAsync(userId));
    }

    [Fact]
    public async Task Refresh_ReplayAfterGrace_RevokesEverything()
    {
        // Outside the window the old rule stands: a rotated token presented again is theft.
        var (userId, first) = await RegisterAndLoginAsync("grace-expired");
        var second = await RefreshAsync(first.RefreshToken);
        await AgeRevocationAsync(first.RefreshToken, TimeSpan.FromMinutes(3));

        var replay = await _authService.RefreshAsync(first.RefreshToken);

        Assert.False(replay.Succeeded);
        Assert.NotNull(await RevokedAtAsync(second.RefreshToken));
        Assert.Equal(0, await LiveTokenCountAsync(userId));
    }

    [Fact]
    public async Task Refresh_ReplayWithinGrace_DoesNotRevokeOlderSessions()
    {
        var (userId, phone) = await RegisterAndLoginAsync("grace-other-device");
        var tablet = await LoginAsync(userId);
        await RefreshAsync(phone.RefreshToken);

        Assert.True((await _authService.RefreshAsync(phone.RefreshToken)).Succeeded);

        Assert.Null(await RevokedAtAsync(tablet.RefreshToken));
    }

    [Fact]
    public async Task Refresh_ReplayWithinGrace_AfterTheSuccessorWasUsed_RevokesEverything()
    {
        // A client that received T2 would present T2, not T1. Once T2 has been used, T1 coming back
        // means two holders - the grace no longer applies.
        var (userId, first) = await RegisterAndLoginAsync("grace-successor-used");
        var second = await RefreshAsync(first.RefreshToken);
        await RefreshAsync(second.RefreshToken);

        var replay = await _authService.RefreshAsync(first.RefreshToken);

        Assert.False(replay.Succeeded);
        Assert.Equal(0, await LiveTokenCountAsync(userId));
    }

    [Fact]
    public async Task Refresh_LoggedOutToken_IsNotRevivedByTheGrace()
    {
        // Logout revokes without a successor, so the grace can never bring a logged-out token back.
        var (userId, tokens) = await RegisterAndLoginAsync("grace-logout");
        await _authService.LogoutAsync(userId, tokens.RefreshToken);
        var newSession = await LoginAsync(userId);

        var replay = await _authService.RefreshAsync(tokens.RefreshToken);

        Assert.False(replay.Succeeded);
        Assert.NotNull(await RevokedAtAsync(newSession.RefreshToken));
    }

    [Fact]
    public async Task Refresh_RotatedTokenAfterPasswordChange_IsNotRevivedByTheGrace()
    {
        // A password change is what someone does after a theft. The pre-change token must stay
        // dead even inside the two-minute window, or the thief walks straight back in.
        var (userId, first) = await RegisterAndLoginAsync("grace-password-change");
        await RefreshAsync(first.RefreshToken);
        var changed = await _authService.ChangePasswordAsync(userId,
            new ChangePasswordRequest { CurrentPassword = "correct-horse-battery-staple", NewPassword = FreshPassword() });
        Assert.True(changed.Succeeded);

        var replay = await _authService.RefreshAsync(first.RefreshToken);

        Assert.False(replay.Succeeded);
    }

    [Fact]
    public async Task Refresh_SameTokenConcurrently_LeavesAtMostOneLiveSession()
    {
        var (userId, tokens) = await RegisterAndLoginAsync("grace-race");

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var dbContext = NewDbContext();
            await NewAuthService(dbContext).RefreshAsync(tokens.RefreshToken);
        }));

        Assert.True(await LiveTokenCountAsync(userId) <= 1);
    }

    [Fact]
    public async Task Refresh_AbortedBeforeTheNewPairIsSaved_LeavesTheOldTokenUsable()
    {
        // The client's refresh timeout cancels the request between the rotation and the insert of
        // the new pair. Committed apart, that left the old token rotated with no successor row, so
        // the client's retry found nothing to discard and signed the user out everywhere.
        var (userId, tokens) = await RegisterAndLoginAsync("refresh-aborted");
        await using (var dbContext = NewDbContext())
        {
            var aborting = NewAuthService(dbContext, new StubJwtTokenService { AbortNextAccessToken = true });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => aborting.RefreshAsync(tokens.RefreshToken));
        }

        var retry = await _authService.RefreshAsync(tokens.RefreshToken);

        Assert.True(retry.Succeeded);
        Assert.Equal(1, await LiveTokenCountAsync(userId));
    }

    // ---- Reset-code races (#237) -----------------------------------------------------------

    [Fact]
    public async Task ResetPassword_ConcurrentWrongGuesses_CountEveryAttempt()
    {
        // Each request on its own connection, or one DbContext would serialise the race away.
        var email = await RegisterAsync("reset-race-guess");
        var userId = await UserIdAsync(email);
        var code = await RequestCodeAsync(email);
        var wrong = WrongCode(code);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var dbContext = NewDbContext();
            await NewAuthService(dbContext).ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = wrong, NewPassword = FreshPassword() });
        }));

        var attempts = await _dbContext.PasswordResetCodes.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.AttemptCount)
            .SingleAsync();
        Assert.Equal(5, attempts);

        var withRightCode = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() });
        Assert.False(withRightCode.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_CorrectCodeTwiceConcurrently_SucceedsOnce()
    {
        var email = await RegisterAsync("reset-race-double-use");
        var code = await RequestCodeAsync(email);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var dbContext = NewDbContext();
            return await NewAuthService(dbContext).ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() });
        }));

        Assert.Single(results, r => r.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_FourWrongThenRight_StillSucceeds()
    {
        // The correct guess is counted too, so the cap is five submissions - the fifth may be right.
        var email = await RegisterAsync("reset-fifth-try");
        var code = await RequestCodeAsync(email);
        for (var i = 0; i < 4; i++)
        {
            await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = WrongCode(code), NewPassword = FreshPassword() });
        }

        var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() });

        Assert.True(result.Succeeded);
    }

    // ---- Password change/reset atomicity (#246) --------------------------------------------

    [Fact]
    public async Task ChangePassword_WhenRevokingSessionsFails_ChangesNothing()
    {
        // The password used to commit before the sessions were revoked. A failure in between left
        // the new password in place and every old refresh token live - the tokens the user was
        // changing their password to kill.
        var (userId, tokens) = await RegisterAndLoginAsync("change-atomic");
        await using (var failing = NewDbContext(new FailingCommandInterceptor("UPDATE refresh_tokens")))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => NewAuthService(failing).ChangePasswordAsync(userId,
                new ChangePasswordRequest { CurrentPassword = "correct-horse-battery-staple", NewPassword = FreshPassword() }));
        }

        Assert.Equal("hashed:correct-horse-battery-staple", await StoredPasswordHashAsync(userId));
        Assert.Null(await RevokedAtAsync(tokens.RefreshToken));
    }

    [Fact]
    public async Task ResetPassword_WhenRevokingSessionsFails_ChangesNothing_AndTheCodeStillWorks()
    {
        var email = await RegisterAsync("reset-atomic");
        var userId = await UserIdAsync(email);
        var tokens = await LoginAsync(userId);
        var code = await RequestCodeAsync(email);
        await using (var failing = NewDbContext(new FailingCommandInterceptor("UPDATE refresh_tokens")))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => NewAuthService(failing).ResetPasswordAsync(
                new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() }));
        }

        Assert.Equal("hashed:correct-horse-battery-staple", await StoredPasswordHashAsync(userId));
        Assert.Null(await RevokedAtAsync(tokens.RefreshToken));
        var retry = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() });
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_AbortedWhileSavingThePassword_KeepsTheCode()
    {
        // The code used to be consumed in its own committed statement, so a request that died
        // before the password was saved spent the code and changed nothing.
        var email = await RegisterAsync("reset-aborted");
        var code = await RequestCodeAsync(email);
        using var abort = new CancellationTokenSource();
        var hasher = new CountingPasswordHasher { OnHash = abort.Cancel };
        await using (var dbContext = NewDbContext())
        {
            var aborting = NewAuthService(dbContext, passwordHasher: hasher);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => aborting.ResetPasswordAsync(
                new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() }, abort.Token));
        }

        var retry = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() });

        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task ForgotPassword_Concurrent_LeavesOneLiveCode()
    {
        var email = await RegisterAsync("forgot-race");
        var userId = await UserIdAsync(email);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var dbContext = NewDbContext();
            await NewAuthService(dbContext).ForgotPasswordAsync(email);
        }));

        Assert.Equal(1, await _dbContext.PasswordResetCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null));

        // One mail, and the code in it is the live one. Two requests that both passed the throttle
        // used to mail two codes, the second request retiring the code the first had just sent.
        var mailed = Assert.Single(ResetCodesMailedTo(email));
        var reset = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = mailed, NewPassword = FreshPassword() });
        Assert.True(reset.Succeeded);
    }

    // ---- Commit acknowledgement lost, transaction replayed by the execution strategy ---------

    [Fact]
    public async Task ForgotPassword_WhenTheCommitReplyIsLost_StillMailsTheLiveCode()
    {
        // The replay used to find the code the first attempt had committed, take it for the
        // cooldown, and send nothing - leaving a live code the user never received.
        var email = await RegisterAsync("forgot-lost-commit");
        var interceptor = new LostCommitReplyInterceptor();
        await using (var dbContext = NewDbContext(interceptor))
        {
            await NewAuthService(dbContext).ForgotPasswordAsync(email);
        }

        Assert.True(interceptor.Tripped);
        var mailed = Assert.Single(ResetCodesMailedTo(email));
        var reset = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = mailed, NewPassword = FreshPassword() });
        Assert.True(reset.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_WhenTheCommitReplyIsLost_ReportsTheSuccess()
    {
        // The replay found the code already consumed and answered "invalid code" for a reset that
        // had changed the password.
        var email = await RegisterAsync("reset-lost-commit");
        var userId = await UserIdAsync(email);
        var code = await RequestCodeAsync(email);
        var newPassword = FreshPassword();
        var interceptor = new LostCommitReplyInterceptor();
        ResetPasswordResult result;
        await using (var dbContext = NewDbContext(interceptor))
        {
            result = await NewAuthService(dbContext).ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = newPassword });
        }

        Assert.True(interceptor.Tripped);
        Assert.True(result.Succeeded);
        Assert.Equal($"hashed:{newPassword}", await StoredPasswordHashAsync(userId));
    }

    // ---- Forgot-password throttle (#238) ---------------------------------------------------

    [Fact]
    public async Task ForgotPassword_TwiceWithinCooldown_SendsOneMail_AndFirstCodeStillWorks()
    {
        var email = await RegisterAsync("forgot-cooldown");
        var code = await RequestCodeAsync(email);

        await _authService.ForgotPasswordAsync(email);

        Assert.Single(ResetCodesMailedTo(email));
        var reset = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = email, Code = code, NewPassword = FreshPassword() });
        Assert.True(reset.Succeeded);
    }

    [Fact]
    public async Task ForgotPassword_SixTimesInAnHour_IssuesAtMostFive()
    {
        var email = await RegisterAsync("forgot-hourly");
        var userId = await UserIdAsync(email);

        for (var i = 0; i < 6; i++)
        {
            await _authService.ForgotPasswordAsync(email);
            await AgeResetCodesAsync(userId, TimeSpan.FromSeconds(61));
        }

        Assert.Equal(5, ResetCodesMailedTo(email).Count);

        // Once the codes leave the rolling hour, the account can ask again.
        await AgeResetCodesAsync(userId, TimeSpan.FromHours(1));
        await _authService.ForgotPasswordAsync(email);
        Assert.Equal(6, ResetCodesMailedTo(email).Count);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturnsQuietly()
    {
        var email = $"forgot-unknown-{Guid.NewGuid()}@example.com";

        await _authService.ForgotPasswordAsync(email);

        Assert.Empty(ResetCodesMailedTo(email));
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private AppDbContext NewDbContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>(AppDbContextOptionsFactory.Build(_fixture.ConnectionString))
            .AddInterceptors(interceptors)
            .Options);

    private AuthService NewAuthService(AppDbContext dbContext, IJwtTokenService? jwtTokenService = null, IPasswordHasher? passwordHasher = null) =>
        new(dbContext, passwordHasher ?? _passwordHasher, jwtTokenService ?? new StubJwtTokenService(), _emailSender, NullLogger<AuthService>.Instance);

    /// <summary>A throwaway new password, generated so no password-shaped literal sits in the source.</summary>
    private static string FreshPassword() => $"pw-{Guid.NewGuid():N}";

    private static string WrongCode(string code) => ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

    private Task<Guid> UserIdAsync(string email) =>
        _dbContext.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();

    private async Task<string> RegisterAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        await _authService.RegisterAsync(new RegisterRequest { Email = email, Password = "correct-horse-battery-staple" });
        return email;
    }

    private async Task<(Guid UserId, AuthTokenResponse Tokens)> RegisterAndLoginAsync(string prefix)
    {
        var userId = await UserIdAsync(await RegisterAsync(prefix));
        return (userId, await LoginAsync(userId));
    }

    private async Task<AuthTokenResponse> LoginAsync(Guid userId)
    {
        var email = await _dbContext.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync();
        var result = await _authService.LoginAsync(new LoginRequest { Email = email, Password = "correct-horse-battery-staple" });
        Assert.True(result.Succeeded);
        return result.Tokens!;
    }

    private async Task<AuthTokenResponse> RefreshAsync(string refreshToken)
    {
        var result = await _authService.RefreshAsync(refreshToken);
        Assert.True(result.Succeeded);
        return result.Tokens!;
    }

    private Task<DateTimeOffset?> RevokedAtAsync(string plainRefreshToken) =>
        _dbContext.RefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == $"hash:{plainRefreshToken}")
            .Select(t => t.RevokedAt)
            .SingleAsync();

    /// <summary>Read past the change tracker, which still holds the hash from registration.</summary>
    private Task<string> StoredPasswordHashAsync(Guid userId) =>
        _dbContext.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();

    private Task<int> LiveTokenCountAsync(Guid userId) =>
        _dbContext.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null);

    private Task AgeRevocationAsync(string plainRefreshToken, TimeSpan by) =>
        _dbContext.RefreshTokens
            .Where(t => t.TokenHash == $"hash:{plainRefreshToken}")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, t => t.RevokedAt - by));

    private Task AgeResetCodesAsync(Guid userId, TimeSpan by) =>
        _dbContext.PasswordResetCodes
            .Where(c => c.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, c => c.CreatedAt - by));

    private List<string> ResetCodesMailedTo(string email) =>
        _emailSender.Snapshot()
            .Where(m => m.To == email && m.Subject.Contains("reset code"))
            .Select(m => Regex.Match(m.Body, @"\b(\d{6})\b").Groups[1].Value)
            .ToList();

    private async Task<string> RequestCodeAsync(string email)
    {
        var before = ResetCodesMailedTo(email);
        await _authService.ForgotPasswordAsync(email);
        return Assert.Single(ResetCodesMailedTo(email).Except(before));
    }

    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        public int VerifyCalls { get; set; }

        /// <summary>Runs on every hash, so a test can fail a request part way through a reset.</summary>
        public Action? OnHash { get; init; }

        public string Hash(string password)
        {
            OnHash?.Invoke();
            return $"hashed:{password}";
        }

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

            // Locked because the race tests send from several tasks at once.
            lock (Sent)
            {
                Sent.Add((toEmail, subject, body));
            }

            return Task.CompletedTask;
        }

        public List<(string To, string Subject, string Body)> Snapshot()
        {
            lock (Sent)
            {
                return [.. Sent];
            }
        }
    }

    /// <summary>Fails any command whose SQL contains the fragment, as a dropped connection would mid-request.</summary>
    private sealed class FailingCommandInterceptor(string sqlFragment) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            FailIfMatched(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            FailIfMatched(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void FailIfMatched(DbCommand command)
        {
            if (command.CommandText.Contains(sqlFragment, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Simulated failure on {sqlFragment}.");
            }
        }
    }

    /// <summary>
    /// Lets the first commit land, then fails it the way a connection dropped before the reply
    /// does - a transient error, so the execution strategy retries the whole transaction.
    /// </summary>
    private sealed class LostCommitReplyInterceptor : DbTransactionInterceptor
    {
        public bool Tripped { get; private set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Tripped)
            {
                return Task.CompletedTask;
            }

            Tripped = true;
            throw new NpgsqlException("Simulated lost commit reply.", new IOException("Connection reset by peer."));
        }
    }

    private sealed class StubJwtTokenService : IJwtTokenService
    {
        /// <summary>Fails the next pair as an aborted request would, after the rotation has run.</summary>
        public bool AbortNextAccessToken { get; set; }

        public GeneratedAccessToken GenerateAccessToken(Guid userId, string email)
        {
            if (AbortNextAccessToken)
            {
                AbortNextAccessToken = false;
                throw new OperationCanceledException();
            }

            return new($"access-{userId}", DateTimeOffset.UtcNow.AddMinutes(15));
        }

        public GeneratedRefreshToken GenerateRefreshToken()
        {
            var plain = Guid.NewGuid().ToString("N");
            return new GeneratedRefreshToken(plain, HashRefreshToken(plain), DateTimeOffset.UtcNow.AddDays(30));
        }

        public string HashRefreshToken(string plainToken) => $"hash:{plainToken}";
    }
}
