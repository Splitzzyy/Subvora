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
        }
    }

    /// <summary>
    /// Tells the address's real owner that someone tried to register with it. This is the whole
    /// reason register can stay silent: the person entitled to know is told, over a channel only
    /// they control, while the caller learns nothing.
    /// </summary>
    private async Task SendAlreadyRegisteredNoticeAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            await _emailSender.SendAsync(
                email,
                "Someone tried to create a SubVora account with your email",
                "You already have a SubVora account with this address. If this was you, sign in instead - "
                    + "or use \"forgot password\" if you cannot remember it. If it was not you, no action is needed: "
                    + "your account was not changed and no one was let in.",
                cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort. A mail failure must not become a 500, because the response to a
            // duplicate registration has to be indistinguishable from the response to a new one.
        }
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            // Deliberately does the work anyway - see DummyPasswordHash.
            _passwordHasher.Verify(request.Password, DummyPasswordHash.Value);
            return LoginResult.Failed();
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return LoginResult.Failed();
        }

        var tokens = await IssueTokenPairAsync(user, cancellationToken);
        return LoginResult.Success(tokens);
    }

    public async Task<RefreshResult> RefreshAsync(string presentedRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
        {
            return RefreshResult.Failed();
        }

        var presentedHash = _jwtTokenService.HashRefreshToken(presentedRefreshToken);
        var existingToken = await _dbContext.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);
        if (existingToken is null)
        {
            return RefreshResult.Failed();
        }

        var now = DateTimeOffset.UtcNow;
        if (existingToken.ExpiresAt <= now)
        {
            // Simply too old. Not a theft signal, so the rest of the chain stays usable.
            return RefreshResult.Failed();
        }

        // Compare-and-swap, not read-then-write: reading RevokedAt and setting it in two steps
        // lets two callers presenting the same token both see null and both mint a pair, which is
        // exactly the concurrent-theft case rotation exists to catch. The database decides who
        // wins, and only the winner gets a row back.
        var rotated = await _dbContext.RefreshTokens
            .Where(t => t.Id == existingToken.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), cancellationToken);

        if (rotated == 0)
        {
            // Either it was already rotated before we looked, or a concurrent caller rotated it
            // between our read and our update. Both mean this token has been presented twice while
            // still valid - revoke every token for the user rather than just this one.
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
            return RefreshResult.Failed();
        }

        var user = await _dbContext.Users.SingleAsync(u => u.Id == existingToken.UserId, cancellationToken);
        var tokens = await IssueTokenPairAsync(user, cancellationToken);
        return RefreshResult.Success(tokens);
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

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            // No enumeration - caller gets the same outcome either way.
            return;
        }

        // Retire any code still outstanding. Without this every request adds another live code,
        // and since AttemptCount is per row, each one hands out a fresh 5 guesses at the same
        // six-digit space - so "5 attempts" would only ever have bounded a single code.
        var supersededCodes = await _dbContext.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var superseded in supersededCodes)
        {
            superseded.UsedAt = DateTimeOffset.UtcNow;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        _dbContext.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = HashResetCode(code),
            ExpiresAt = DateTimeOffset.UtcNow.Add(PasswordResetCodeLifetime),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

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

        var resetCode = await _dbContext.PasswordResetCodes
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (resetCode is null || resetCode.ExpiresAt <= DateTimeOffset.UtcNow || resetCode.AttemptCount >= MaxPasswordResetAttempts)
        {
            return ResetPasswordResult.Failed();
        }

        if (resetCode.CodeHash != HashResetCode(request.Code))
        {
            resetCode.AttemptCount++;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ResetPasswordResult.Failed();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        resetCode.UsedAt = DateTimeOffset.UtcNow;
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

    public async Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return ChangePasswordResult.Failed();
        }

        // The access token proves the session; this proves the person. Skipping it would let a
        // stolen token be traded for permanent ownership of the account, which is the exact thing
        // changing a password is meant to stop.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Change-password rejected for user {UserId}: current password did not match.", userId);
            return ChangePasswordResult.Failed();
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
        return ChangePasswordResult.Success(tokens);
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

    private async Task<AuthTokenResponse> IssueTokenPairAsync(User user, CancellationToken cancellationToken)
    {
        var accessToken = _jwtTokenService.GenerateAccessToken(user.Id, user.Email);
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
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
