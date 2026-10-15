namespace SubVora.Application.Users;

public class DeleteAccountRequest
{
    /// <summary>
    /// Proof that whoever holds this session also knows the password, as on change-password: a
    /// stolen access token must not be enough to destroy the account and everything in it.
    /// </summary>
    public string CurrentPassword { get; set; } = string.Empty;
}
