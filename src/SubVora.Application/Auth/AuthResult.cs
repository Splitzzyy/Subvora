namespace SubVora.Application.Auth;

/// <summary>
/// Auth operation result carrying a fresh token pair on success.
/// Used by login, refresh, and change-password flows. Changing a password
/// revokes every refresh token the account has, including the caller's own —
/// without a replacement the user is signed out of the device they just used.
/// </summary>
public class AuthResult
{
    public bool Succeeded { get; private init; }
    public AuthTokenResponse? Tokens { get; private init; }

    public static AuthResult Failed() => new() { Succeeded = false };

    public static AuthResult Success(AuthTokenResponse tokens) => new() { Succeeded = true, Tokens = tokens };
}
