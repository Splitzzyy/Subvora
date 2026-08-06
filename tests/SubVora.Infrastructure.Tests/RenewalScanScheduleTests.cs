using SubVora.Application.Scheduling;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// The renewal scan and the FX refresh both used to fire 24h after process start, so their time of
/// day was whatever the last deploy happened to be. These pin the shared replacement schedule
/// without waiting on real time.
/// </summary>
public class RenewalScanScheduleTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void DelayUntilNextRun_BeforeTheTargetHour_WaitsUntilTodaysOccurrence()
    {
        var delay = DailyUtcSchedule.DelayUntilNextRun(Utc(2026, 8, 6, 1, 30), utcHour: 2);

        Assert.Equal(TimeSpan.FromMinutes(30), delay);
    }

    [Fact]
    public void DelayUntilNextRun_AfterTheTargetHour_WaitsUntilTomorrows()
    {
        var delay = DailyUtcSchedule.DelayUntilNextRun(Utc(2026, 8, 6, 14, 0), utcHour: 2);

        Assert.Equal(TimeSpan.FromHours(12), delay);
    }

    [Fact]
    public void DelayUntilNextRun_ExactlyAtTheTargetHour_WaitsAFullDayRatherThanRescanningTheSameHour()
    {
        var delay = DailyUtcSchedule.DelayUntilNextRun(Utc(2026, 8, 6, 2, 0), utcHour: 2);

        Assert.Equal(TimeSpan.FromHours(24), delay);
    }

    [Fact]
    public void DelayUntilNextRun_LateInTheDay_CrossesTheDateBoundaryCorrectly()
    {
        var now = Utc(2026, 8, 6, 23, 45);

        var delay = DailyUtcSchedule.DelayUntilNextRun(now, utcHour: 2);

        Assert.Equal(TimeSpan.FromMinutes(135), delay);
        Assert.Equal(Utc(2026, 8, 7, 2, 0), now + delay);
    }

    [Fact]
    public void DelayUntilNextRun_AcrossAMonthEnd_LandsOnTheFirstOfTheNextMonth()
    {
        var now = Utc(2026, 8, 31, 23, 0);

        var delay = DailyUtcSchedule.DelayUntilNextRun(now, utcHour: 2);

        Assert.Equal(Utc(2026, 9, 1, 2, 0), now + delay);
    }

    [Fact]
    public void DelayUntilNextRun_AtMidnightHour_IsSupported()
    {
        var delay = DailyUtcSchedule.DelayUntilNextRun(Utc(2026, 8, 6, 23, 0), utcHour: 0);

        Assert.Equal(TimeSpan.FromHours(1), delay);
    }

    [Fact]
    public void ReadUtcHour_ValidValue_IsUsed()
    {
        Assert.Equal(5, DailyUtcSchedule.ReadUtcHour("5", defaultHour: 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("half past two")]
    [InlineData("-1")]
    [InlineData("24")]
    public void ReadUtcHour_AbsentOrNonsense_FallsBackRatherThanFailingStartup(string? configured)
    {
        Assert.Equal(2, DailyUtcSchedule.ReadUtcHour(configured, defaultHour: 2));
    }
}
