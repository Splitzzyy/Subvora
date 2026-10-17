namespace SubVora.Application.Auth;

/// <summary>
/// Says whether a password appears in a public breach corpus. Credential stuffing replays exactly
/// those passwords, one guess per account, so no per-account lockout ever sees it coming.
/// </summary>
public interface IBreachedPasswordChecker
{
    /// <summary>False when the corpus cannot be asked: a third party must not decide who can register.</summary>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default);
}
