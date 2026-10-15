using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SubVora.Application.Auth;
using SubVora.Application.Notifications;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private static readonly TimeSpan PasswordResetCodeLifetime = TimeSpan.FromMinutes(15);
    private const int MaxPasswordResetAttempts = 5;

    /// <summary>
    /// A second forgot-password for the same account inside this window is accepted and ignored.
    /// Every issue retires the code before it, so without a cooldown anyone who knows an address
    /// can keep killing the code its owner is about to type.
    /// </summary>
    private static readonly TimeSpan ResetCodeCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// At most this many codes per account per rolling hour. Bounds how much of the app-wide mail
    /// quota (Brevo free: 300/day) one targeted address can burn.
    /// </summary>
    private const int MaxResetCodesPerHour = 5;

    /// <summary>
    /// How long after a rotation the token it replaced may be presented once more. A mobile
    /// client that times out before the response arrives (a Render cold start takes longer than
    /// its refresh timeout) still holds the old token while the server has already rotated it;
    /// without this window its retry reads as theft and signs the user out of every device.
    ///
    /// Only applies while the successor has never been used - a client that received it would
    /// present that, not the old one - and the replay revokes that successor before minting a new
    /// pair, so there is still one live chain. The cost, accepted deliberately: for two minutes a
    /// stolen copy of the previous token can take over the session. The victim's next refresh then
    /// finds its own token discarded and, outside the window, trips the full revoke-all - the same
    /// end state as without the grace, one refresh later. Hosted identity providers ship the same
    /// trade-off as a "reuse interval".
    /// </summary>
    private static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Verified against when the email is unknown, so a missing account costs the same ~250ms of
    /// BCrypt as a wrong password instead of answering instantly. Without it, response time alone
    /// tells an attacker which addresses are registered. Computed once at startup rather than
    /// embedded as a literal, which would read as a checked-in credential.
    /// </summary>
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString(), workFactor: 12));

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IEmailSender emailSender,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Hashed before the existence check, and unconditionally: BCrypt at work factor 12 costs
        // ~250ms, so returning early for a duplicate made "already registered" answer in single-digit
        // milliseconds while a new account took a third of a second. Identical responses are no use
        // if the wait tells you which branch ran.
        var passwordHash = _passwordHasher.Hash(request.Password);

        var emailExists = await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            await SendAlreadyRegisteredNoticeAsync(normalizedEmail, cancellationToken);
            return;
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The AnyAsync check above is a fast path, not a guarantee: two simultaneous
            // registrations of the same address both pass it and race to the unique index on
            // users.email. The loser must end up on the same silent path as a sequential
            // duplicate, not a 500.
            _dbContext.Entry(user).State = EntityState.Detached;
            await SendAlreadyRegisteredNoticeAsync(normalizedEmail, cancellationToken);
            return;
        }

        await SendWelcomeNoticeAsync(normalizedEmail, cancellationToken);
    }

    /// <summary>
    /// Confirms the account to the address that now owns it. Both register branches send exactly
    /// one mail, which is deliberate: the branch that sends nothing is distinguishable from the one
    /// that does the moment anyone watches the mailbox, and the whole endpoint is built so a caller
    /// cannot tell which ran.
    /// </summary>
    private Task SendWelcomeNoticeAsync(string email, CancellationToken cancellationToken) =>
        SendQuietlyAsync(
            email,
            "Welcome to SubVora",
            "Your SubVora account is ready. Sign in on the app to start tracking subscriptions, and "
                + "we will remind you before each renewal so nothing charges you by surprise.\n\n"
                + "If you did not create this account, you can ignore this message - nobody can sign in "
                + "without the password chosen at sign-up.",
            cancellationToken);

    /// <summary>
    /// Tells the address's real owner that someone tried to register with it. This is the whole
    /// reason register can stay silent: the person entitled to know is told, over a channel only
    /// they control, while the caller learns nothing.
    /// </summary>
    private Task SendAlreadyRegisteredNoticeAsync(string email, CancellationToken cancellationToken) =>
        SendQuietlyAsync(
            email,
            "Someone tried to create a SubVora account with your email",
            "You already have a SubVora account with this address. If this was you, sign in instead - "
                + "or use \"forgot password\" if you cannot remember it. If it was not you, no action is needed: "
                + "your account was not changed and no one was let in.",
            cancellationToken);

    /// <summary>
    /// Sends and swallows. A mail failure must not become a 500 on register, because the response
    /// to a duplicate registration has to be indistinguishable from the response to a new one.
    /// </summary>
    private async Task SendQuietlyAsync(string email, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await _emailSender.SendAsync(email, subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            // Logged without the address: mail is the one thing about register that is invisible
            // from the outside, so a silent failure here is otherwise indistinguishable from a
            // working mailer with a slow inbox - which is exactly the report this branch answers.
            _logger.LogWarning(ex, "Could not queue a registration email.");
        }
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            // Deliberately does the work anyway - see DummyPasswordHash.
            _passwordHasher.Verify(request.Password, DummyPasswordHash.Value);
            return AuthResult.Failed();
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return AuthResult.Failed();
        }

        var tokens = await IssueTokenPairAsync(user, cancellationToken);
        return AuthResult.Success(tokens);
    }

    public async Task<AuthResult> RefreshAsync(string presentedRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
        {
            return AuthResult.Failed();
        }

        var presentedHash = _jwtTokenService.HashRefreshToken(presentedRefreshToken);
        var existingToken = await _dbContext.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);
        if (existingToken is null)
        {
            return AuthResult.Failed();
        }

        var now = DateTimeOffset.UtcNow;
        if (existingToken.ExpiresAt <= now)
        {
            // Simply too old. Not a theft signal, so the rest of the chain stays usable.
            return AuthResult.Failed();
        }

        // Compare-and-swap, not read-then-write: reading RevokedAt and setting it in two steps
        // lets two callers presenting the same token both see null and both mint a pair, which is
        // exactly the concurrent-theft case rotation exists to catch. The database decides who
        // wins, and only the winner gets a row back.
        //
        // The successor's id is chosen here and recorded on the old row in the same statement, so
        // a later replay can find exactly the pair this rotation minted (see RefreshReuseGrace).
        var successorId = Guid.NewGuid();
        var rotated = await _dbContext.RefreshTokens
            .Where(t => t.Id == existingToken.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedByTokenId, successorId), cancellationToken);

        if (rotated == 0)
        {
            var reissued = await TryReissueAfterLostResponseAsync(existingToken.Id, now, cancellationToken);
            if (reissued is not null)
            {
                return AuthResult.Success(reissued);
            }

            // Either it was already rotated before we looked, or a concurrent caller rotated it
            // between our read and our update. Both mean this token has been presented twice while
            // still valid - revoke every token for the user rather than just this one. (Unless the
            // replay is inside RefreshReuseGrace with an unused successor, handled just above: that
            // is a lost response, not a second holder.)
            //
            // Known residual: in the concurrent case the winner may insert its replacement pair
            // after this sweep has read the table, so that one pair can survive. What is
            // guaranteed is that at most one caller succeeds and the replayed token is dead - the
            // previous read-then-write let *both* callers mint a pair and raised no signal at all.
            // Closing the remainder needs a per-user "sessions valid from" watermark checked at
            // token validation, which is a schema change and deliberately not done here.
            //
            // Logged because this is the one branch in the service that means "a credential may
            // have been stolen", and it silently signs the user out of every device. Without a line
            // here the support question - why was I logged out everywhere? - has no answer.
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}; revoking all active refresh tokens.",
                existingToken.UserId);

            await RevokeAllActiveTokensForUserAsync(existingToken.UserId, cancellationToken);
            return AuthResult.Failed();
        }

        var user = await _dbContext.Users.SingleAsync(u => u.Id == existingToken.UserId, cancellationToken);
        var tokens = await IssueTokenPairAsync(user, cancellationToken, successorId);
        return AuthResult.Success(tokens);
    }

    /// <summary>
    /// The grace branch of reuse detection: a token rotated moments ago whose successor was never
    /// used is a client retrying after losing the response, not a thief. Discards that successor
    /// and mints a replacement. Returns null when the replay does not qualify, leaving the caller
    /// on the revoke-everything path.
    /// </summary>
    private async Task<AuthTokenResponse?> TryReissueAfterLostResponseAsync(
        Guid presentedTokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Re-read rather than trusting the copy taken before the compare-and-swap: a concurrent
        // rotation may have landed in between, and its values are the ones that count.
        var presented = await _dbContext.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.Id == presentedTokenId, cancellationToken);

        // Logout, password change and reuse detection revoke without a successor, so a token
        // retired by any of those never qualifies.
        if (presented.ReplacedByTokenId is not { } successorId
            || presented.RevokedAt is not { } rotatedAt
            || now - rotatedAt > RefreshReuseGrace)
        {
            return null;
        }

        // Compare-and-swap on the successor: it must still be unused, and of two concurrent
        // replays only one gets to discard it. The loser falls through to reuse detection.
        var discarded = await _dbContext.RefreshTokens
            .Where(t => t.Id == successorId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);
        if (discarded == 0)
        {
            return null;
        }

        // Re-pointed so a second lost response inside the same window is recoverable too. The
        // window itself still runs from the original rotation.
        var replacementId = Guid.NewGuid();
        await _dbContext.RefreshTokens
            .Where(t => t.Id == presentedTokenId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ReplacedByTokenId, replacementId), cancellationToken);

        _logger.LogInformation(
            "Refresh token replayed within grace for user {UserId}; treated as a lost response.",
            presented.UserId);

        var user = await _dbContext.Users.SingleAsync(u => u.Id == presented.UserId, cancellationToken);
        return await IssueTokenPairAsync(user, cancellationToken, replacementId);
    }

    public async Task LogoutAsync(Guid userId, string presentedRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
        {
            return;
        }

        var presentedHash = _jwtTokenService.HashRefreshToken(presentedRefreshToken);
        var token = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == presentedHash && t.UserId == userId, cancellationToken);

        // Idempotent and quiet on a missing/foreign/already-revoked token - logout should
        // never leak whether a given token string exists or belongs to someone else.
        if (token is null || token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Generated and hashed before the lookup, and unconditionally - the same shape RegisterAsync
        // uses for its BCrypt hash and LoginAsync for DummyPasswordHash. Both branches now pay for
        // the CSPRNG draw and the SHA-256, so that much of the work no longer depends on whether the
        // address exists.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var codeHash = HashResetCode(code);

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            // No enumeration by status or body - the caller gets the same 200 and empty response
            // either way, and the work above is done regardless.
            //
            // Residual, accepted deliberately: a real account additionally performs the throttle
            // SELECT, an UPDATE retiring outstanding codes and an INSERT, so a known address still
            // costs more round trips than an unknown one. Closing that fully would
            // mean issuing throwaway writes for addresses with no account - real write load driven
            // by anyone who can reach the endpoint. Judged not worth it: the remaining signal is a
            // few milliseconds of database time rather than the ~250ms BCrypt asymmetry the register
            // and login paths exist to erase, and this endpoint is IP rate-limited (the "auth"
            // policy, 10/min by default) which bounds how finely it can be sampled.
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Per-account throttle, invisible from outside: the caller gets the same empty 200 whether
        // a code went out or not. Counted over created_at, which includes codes already retired -
        // a superseded code still spent a mail.
        var hourAgo = now - TimeSpan.FromHours(1);
        var recentIssues = await _dbContext.PasswordResetCodes.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.CreatedAt >= hourAgo)
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        if (recentIssues.Count >= MaxResetCodesPerHour || recentIssues.Any(issuedAt => now - issuedAt < ResetCodeCooldown))
        {
            _logger.LogInformation("Password reset throttled for user {UserId}.", user.Id);
            return;
        }

        // Retire any code still outstanding. Without this every request adds another live code,
        // and since AttemptCount is per row, each one hands out a fresh 5 guesses at the same
        // six-digit space - so "5 attempts" would only ever have bounded a single code. One
        // statement rather than load-then-save, and the partial unique index
        // ux_password_reset_codes_live_per_user backs it against two concurrent requests.
        await _dbContext.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, now), cancellationToken);

        var resetCode = new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = codeHash,
            ExpiresAt = now.Add(PasswordResetCodeLifetime),
            CreatedAt = now,
        };
        _dbContext.PasswordResetCodes.Add(resetCode);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent request for the same account inserted its live code between our
            // retire and our insert. That request is sending a code, so this one stays quiet.
            _dbContext.Entry(resetCode).State = EntityState.Detached;
            _logger.LogDebug("Concurrent password reset for user {UserId}; another request issued the code.", user.Id);
            return;
        }

        await _emailSender.SendAsync(
            user.Email,
            "Your SubVora password reset code",
            $"Your password reset code is {code}. It expires in 15 minutes.",
            cancellationToken);
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return ResetPasswordResult.Failed();
        }

        var resetCode = await _dbContext.PasswordResetCodes.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (resetCode is null)
        {
            return ResetPasswordResult.Failed();
        }

        // The attempt is counted by the database before the guess is compared, and only while the
        // code is still usable. Reading AttemptCount and writing back +1 let N parallel guesses all
        // read 0 and all pass the cap, against a code space small enough that the cap is the whole
        // defence. Counting the correct guess too makes the cap "5 submissions per code".
        var now = DateTimeOffset.UtcNow;
        var counted = await _dbContext.PasswordResetCodes
            .Where(c => c.Id == resetCode.Id && c.UsedAt == null && c.ExpiresAt > now && c.AttemptCount < MaxPasswordResetAttempts)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.AttemptCount, c => c.AttemptCount + 1), cancellationToken);
        if (counted == 0 || resetCode.CodeHash != HashResetCode(request.Code))
        {
            return ResetPasswordResult.Failed();
        }

        // Consumed by compare-and-swap, so two concurrent requests carrying the right code cannot
        // both set a password: only the one that flips used_at goes on.
        var consumed = await _dbContext.PasswordResetCodes
            .Where(c => c.Id == resetCode.Id && c.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return ResetPasswordResult.Failed();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // A reset is what someone does when they believe the account is compromised, so it has to
        // evict whoever is already in - refresh tokens live 30 days and would otherwise keep
        // minting access tokens off the old password. Any future change-password endpoint owes the
        // user the same revocation.
        //
        // Saved separately above rather than relying on this call's SaveChangesAsync: the password
        // change is the point of the request, and it should not stop persisting because someone
        // later adds an early return to a revocation helper.
        await RevokeAllActiveTokensForUserAsync(user.Id, cancellationToken);

        return ResetPasswordResult.Success();
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return AuthResult.Failed();
        }

        // The access token proves the session; this proves the person. Skipping it would let a
        // stolen token be traded for permanent ownership of the account, which is the exact thing
        // changing a password is meant to stop.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Change-password rejected for user {UserId}: current password did not match.", userId);
            return AuthResult.Failed();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Every existing session dies, exactly as it does on reset: refresh tokens live 30 days and
        // would otherwise keep minting access tokens off the password that was just replaced.
        await RevokeAllActiveTokensForUserAsync(user.Id, cancellationToken);

        // Then a new pair, after the sweep so it survives it. The caller changed their own
        // password; signing them out of the device they are holding would read as a bug, while
        // every other device is now evicted.
        var tokens = await IssueTokenPairAsync(user, cancellationToken);
        return AuthResult.Success(tokens);
    }

    /// <summary>
    /// Npgsql surfaces a unique-index violation as SQLSTATE 23505. Matching on the state code
    /// rather than the message keeps this working across locales and server versions.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string HashResetCode(string plainCode)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plainCode));
        return Convert.ToHexStringLower(hashBytes);
    }

    private async Task<AuthTokenResponse> IssueTokenPairAsync(User user, CancellationToken cancellationToken, Guid? refreshTokenId = null)
    {
        var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Email);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            // Guid.Empty leaves the key to the database default, as before.
            Id = refreshTokenId ?? Guid.Empty,
            UserId = user.Id,
            TokenHash = refreshToken.Hash,
            ExpiresAt = refreshToken.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenResponse
        {
            AccessToken = accessToken.Token,
            AccessTokenExpiresAt = accessToken.ExpiresAt,
            RefreshToken = refreshToken.PlainToken,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
        };
    }

    private async Task RevokeAllActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
