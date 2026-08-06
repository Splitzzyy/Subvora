namespace SubVora.Application.Scheduling;

/// <summary>
/// Shared schedule for the daily background jobs. A fixed UTC hour rather than "24h after process
/// start": a midday deploy used to move every job to midday, and a restart slightly under 24h
/// later could skip a calendar day outright. Pure date arithmetic, so the schedule can be tested
/// without waiting on real time.
/// </summary>
public static class DailyUtcSchedule
{
    /// <summary>Time from <paramref name="now"/> until the next occurrence of <paramref name="utcHour"/>.</summary>
    public static TimeSpan DelayUntilNextRun(DateTimeOffset now, int utcHour)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(utcHour);

        // Strictly-later, so a pass that finishes inside its own scheduled hour waits for tomorrow
        // rather than spinning through the rest of the hour re-running.
        return (today > now ? today : today.AddDays(1)) - now;
    }

    /// <summary>
    /// Parses a configured UTC hour. An absent or nonsense value falls back rather than failing
    /// startup - a mistyped hour should not take the API down. (int.TryParse rather than
    /// GetValue&lt;int?&gt;, which lives in a Binder package this project doesn't reference.)
    /// </summary>
    public static int ReadUtcHour(string? configuredValue, int defaultHour) =>
        int.TryParse(configuredValue, out var hour) && hour is >= 0 and <= 23 ? hour : defaultHour;
}
