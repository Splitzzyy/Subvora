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

    /// <summary>How many charges in this category have gone unpaid past their date.</summary>
    public int OverdueCount => this.Count(s => s.IsOverdue);

    /// <summary>
    /// Right-hand side of the group header. Money owed outranks money due, so an overdue count
    /// replaces the usual "next ..." line rather than sitting alongside it.
    /// </summary>
    public string Summary
    {
        get
        {
            var subscriptions = $"{Count} {(Count == 1 ? "subscription" : "subscriptions")}";

            return OverdueCount > 0
                ? $"{subscriptions} · {OverdueCount} overdue"
                : $"{subscriptions} · next {RelativeDate.Describe(NextBillingDate)}";
        }
    }
}
