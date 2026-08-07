using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.ViewModels;

/// <summary>
/// One category's subscriptions, ordered soonest-to-bill first. CollectionView's grouping wants a
/// collection of collections, hence deriving from List rather than wrapping one.
/// </summary>
public class SubscriptionGroup : List<SubscriptionDto>
{
    /// <summary>Shown when a subscription has no category, so those rows are still reachable rather than dropped.</summary>
    public const string UncategorisedName = "Uncategorised";

    public SubscriptionGroup(string categoryName, IEnumerable<SubscriptionDto> subscriptions)
        : base(subscriptions)
    {
        CategoryName = categoryName;
    }

    public string CategoryName { get; }

    /// <summary>
    /// The soonest billing date in this group. Drives the group's own position in the list, so the
    /// category being charged next sits at the top.
    /// </summary>
    public DateOnly NextBillingDate => this.Min(s => s.NextBillingDate);

    /// <summary>Right-hand side of the group header: how many, and when the next charge lands.</summary>
    public string Summary => $"{Count} {(Count == 1 ? "subscription" : "subscriptions")} · next {RelativeDate.Describe(NextBillingDate)}";
}
