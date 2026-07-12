namespace SubVora.Application.Auth;

public class ResetPasswordResult
{
    public bool Succeeded { get; private init; }

    public static ResetPasswordResult Failed() => new() { Succeeded = false };

    public static ResetPasswordResult Success() => new() { Succeeded = true };
}
