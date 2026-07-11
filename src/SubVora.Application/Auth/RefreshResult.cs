namespace SubVora.Application.Auth;

public class RefreshResult
{
    public bool Succeeded { get; private init; }
    public AuthTokenResponse? Tokens { get; private init; }

    public static RefreshResult Failed() => new() { Succeeded = false };

    public static RefreshResult Success(AuthTokenResponse tokens) => new() { Succeeded = true, Tokens = tokens };
}
