namespace SubVora.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// The token this one was rotated into, set only by a refresh. Logout, password change and
    /// reuse detection revoke without setting it, which is what keeps them out of the refresh
    /// reuse grace (see <c>AuthService.RefreshReuseGrace</c>).
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// The sign-in this token descends from. A login, reset or password change starts a family;
    /// every rotation inherits it, so a theft signal can end one device's chain without signing the
    /// user out of the others, and the chain's age can be bounded however actively it rotates.
    /// </summary>
    public Guid FamilyId { get; set; }

    /// <summary>When the family's sign-in happened - the clock <c>AuthService.AbsoluteSessionLifetime</c> runs on.</summary>
    public DateTimeOffset FamilyStartedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
