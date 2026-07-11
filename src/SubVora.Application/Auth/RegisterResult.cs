namespace SubVora.Application.Auth;

public class RegisterResult
{
    public bool EmailAlreadyExists { get; private init; }
    public RegisteredUserResponse? User { get; private init; }

    public static RegisterResult Conflict() => new() { EmailAlreadyExists = true };

    public static RegisterResult Created(RegisteredUserResponse user) => new() { User = user };
}
