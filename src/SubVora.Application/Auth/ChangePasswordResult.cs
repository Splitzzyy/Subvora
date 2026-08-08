namespace SubVora.Application.Auth;

/// <summary>
/// Carries a fresh token pair on success. Changing a password revokes every refresh token the
/// account has, including the caller's own - without a replacement the user is signed out of the
/// device they just used, which reads as the app breaking rather than as a security measure.
/// </summary>
public class ChangePasswordResult
{
    public bool Succeeded { get; private init; }
    public AuthTokenResponse? Tokens { get; private init; }

    public static ChangePasswordResult Failed() => new() { Succeeded = false };

    public static ChangePasswordResult Success(AuthTokenResponse tokens) => new() { Succeeded = true, Tokens = tokens };
}
