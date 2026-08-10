namespace SubVora.Application.Auth;

/// <summary>
/// The outcome of an auth attempt that either produced a token pair or produced nothing: login,
/// refresh and change-password all answer this shape, and <c>AuthController</c> reads all three the
/// same way - check <see cref="Succeeded"/>, return the tokens or an error.
/// <para>
/// One type rather than three identical ones. <c>ResetPasswordResult</c> stays separate because it
/// genuinely differs: a reset issues no pair, and folding it in here would make "succeeded with no
/// tokens" representable on the three calls where it must never happen.
/// </para>
/// </summary>
public sealed record AuthResult(bool Succeeded, AuthTokenResponse? Tokens)
{
    public static AuthResult Failed() => new(false, null);

    public static AuthResult Success(AuthTokenResponse tokens) => new(true, tokens);
}
