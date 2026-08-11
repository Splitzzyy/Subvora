namespace SubVora.Domain.Enums;

public enum BillingCycleType
{
    Weekly,
    Monthly,
    Yearly,
    OneTime,

    /// <summary>
    /// Appended rather than slotted in after <see cref="Monthly"/>, where it reads better. The
    /// mobile SQLite cache stores this enum as its ordinal (sqlite-net-pcl's default), so inserting
    /// a value mid-list renumbers everything after it and every cached <c>Yearly</c> row on an
    /// already-installed device would come back as <c>Quarterly</c> until the next successful sync.
    /// Postgres maps by label, not ordinal, so it does not care either way; the picker orders itself
    /// explicitly (see <c>SubscriptionDetailViewModel.BillingCycleTypes</c>).
    /// </summary>
    Quarterly,
}
