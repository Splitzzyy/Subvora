namespace SubVora.Application.Auth;

public record GeneratedAccessToken(string Token, DateTimeOffset ExpiresAt);

public record GeneratedRefreshToken(string PlainToken, string Hash, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    GeneratedAccessToken GenerateAccessToken(Guid userId, string email);

    GeneratedRefreshToken GenerateRefreshToken();

    string HashRefreshToken(string plainToken);
}
