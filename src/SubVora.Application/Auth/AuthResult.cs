namespace SubVora.Application.Auth;

public class AuthResult
{
    public bool Succeeded { get; private init; }
    public AuthTokenResponse? Tokens { get; private init; }

    public static AuthResult Failed() => new() { Succeeded = false };

    public static AuthResult Success(AuthTokenResponse tokens) => new() { Succeeded = true, Tokens = tokens };
}
