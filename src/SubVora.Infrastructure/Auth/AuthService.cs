using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SubVora.Application.Auth;
using SubVora.Application.Notifications;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private static readonly TimeSpan PasswordResetCodeLifetime = TimeSpan.FromMinutes(15);
    private const int MaxPasswordResetAttempts = 5;

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEmailSender _emailSender;

    public AuthService(AppDbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IEmailSender emailSender)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _emailSender = emailSender;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailExists = await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            return RegisterResult.Conflict();
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(request.Password),
            PreferredCurrency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return RegisterResult.Created(new RegisteredUserResponse { Id = user.Id, Email = user.Email });
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
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
        var existingToken = await _dbContext.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);
        if (existingToken is null)
        {
            return RefreshResult.Failed();
        }

        if (existingToken.RevokedAt is not null)
        {
            // Reuse of an already-rotated token is a signal of token theft - revoke the
            // whole chain (every active token for this user) rather than just this one.
            await RevokeAllActiveTokensForUserAsync(existingToken.UserId, cancellationToken);
            return RefreshResult.Failed();
        }

        if (existingToken.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return RefreshResult.Failed();
        }

        var user = await _dbContext.Users.SingleAsync(u => u.Id == existingToken.UserId, cancellationToken);

        existingToken.RevokedAt = DateTimeOffset.UtcNow;
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

        return ResetPasswordResult.Success();
    }

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
