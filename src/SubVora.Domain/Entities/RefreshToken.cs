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
    public DateTimeOffset CreatedAt { get; set; }
}
