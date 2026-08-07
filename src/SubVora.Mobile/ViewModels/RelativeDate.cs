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
    /// Dates already past are called out rather than shown as a negative count - the server's
    /// billing-date job rolls them forward, so a past date means the app is looking at stale data.
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
