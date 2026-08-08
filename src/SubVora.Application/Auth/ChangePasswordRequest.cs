namespace SubVora.Application.Auth;

public class ChangePasswordRequest
{
    /// <summary>
    /// Proof that whoever is holding this session also knows the password. Without it a stolen
    /// access token is enough to take the account permanently, which is the one thing changing a
    /// password is supposed to prevent.
    /// </summary>
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}
