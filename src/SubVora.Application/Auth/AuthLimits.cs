namespace SubVora.Application.Auth;

public static class AuthLimits
{
    /// <summary>
    /// users.email is varchar(255). Validators reject anything longer up front; otherwise Postgres
    /// raises 22001 on insert and register answers 500 instead of 400. UserConfiguration reads the
    /// same constant, so the column and the rule cannot drift apart again.
    /// </summary>
    public const int EmailMaxLength = 255;
}
