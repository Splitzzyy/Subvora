namespace SubVora.Mobile.ViewModels;

/// <summary>
/// Turns a billing date into the phrase a user actually reads it as. "in 3 days" answers the
/// question the subscriptions list exists to answer - what is about to be charged - which a bare
/// date does not.
/// </summary>
public static class RelativeDate
{
    /// <summary>
    /// Near dates are described relative to <paramref name="today"/>; anything beyond a fortnight
    /// falls back to a calendar date, because "in 63 days" is harder to place than "9 Oct".
    /// <para>
    /// Dates already past are called out rather than shown as a negative count. Nothing advances
    /// <c>next_billing_date</c> on a timer, so a past date is not stale data - it is the app saying
    /// the charge is genuinely outstanding, which is what <c>SubscriptionDto.IsOverdue</c> reads.
    /// It moves only when the user marks the charge paid.
    /// </para>
    /// </summary>
    public static string Describe(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;

        return days switch
        {
            < 0 => $"overdue since {date:d MMM}",
            0 => "today",
            1 => "tomorrow",
            <= 14 => $"in {days} days",
            _ => date.ToString("d MMM"),
        };
    }

    public static string Describe(DateOnly date) => Describe(date, DateOnly.FromDateTime(DateTime.Today));
}
