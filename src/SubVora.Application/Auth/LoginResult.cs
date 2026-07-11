namespace SubVora.Application.Auth;

public class LoginResult
{
    public bool Succeeded { get; private init; }
    public AuthTokenResponse? Tokens { get; private init; }

    public static LoginResult Failed() => new() { Succeeded = false };

    public static LoginResult Success(AuthTokenResponse tokens) => new() { Succeeded = true, Tokens = tokens };
}
