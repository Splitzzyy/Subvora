namespace SubVora.Application;

/// <summary>
/// Hard per-account caps, checked on create. Every account shares one 0.5 GB free-tier database, so
/// without them one account looping a create could fill it and fail every write for everyone. Each
/// sits at about ten times what a heavy real user keeps, and with them in place every list endpoint
/// is bounded too - the reason none of them paginates.
/// </summary>
public static class UserQuotas
{
    public const int Subscriptions = 500;

    /// <summary>Categories the user created; the shared system defaults do not count.</summary>
    public const int Categories = 100;

    public const int PaymentSources = 50;
}
