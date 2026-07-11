using Microsoft.EntityFrameworkCore;
using SubVora.Application.Auth;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(AppDbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
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
