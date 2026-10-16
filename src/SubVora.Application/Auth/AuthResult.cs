namespace SubVora.Application.Auth;

/// <summary>
/// The outcome of an auth attempt that either produced a token pair or produced nothing: login,
/// refresh, change-password and reset-password all answer this shape, and <c>AuthController</c>
/// reads them the same way - check <see cref="Succeeded"/>, return the tokens or an error.
/// <para>
/// One type rather than four identical ones. A reset used to issue no pair and had its own result
/// type; it signs the user in now, so recovery does not depend on a login a stranger can lock.
/// </para>
/// </summary>
public sealed record AuthResult(bool Succeeded, AuthTokenResponse? Tokens)
{
    public static AuthResult Failed() => new(false, null);

    public static AuthResult Success(AuthTokenResponse tokens) => new(true, tokens);
}
