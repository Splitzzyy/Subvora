using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Notifications;

namespace SubVora.Mobile.Tests;

public class RenewalNotificationPlannerTests
{
    private static readonly DateTime Now = new(2026, 8, 7, 10, 0, 0, DateTimeKind.Local);

    private static SubscriptionDto Subscription(
        string name,
        int daysUntilBilling,
        int alertDaysAdvance = 3,
        bool isActive = true,
        decimal cost = 15.99m,
        BillingCycleType cadence = BillingCycleType.Monthly) => new()
        {
            Id = Guid.NewGuid(),
            CustomName = name,
            CostAmount = cost,
            Currency = "USD",
            CycleCadence = cadence,
            NextBillingDate = DateOnly.FromDateTime(Now).AddDays(daysUntilBilling),
            AlertDaysAdvance = alertDaysAdvance,
            IsActive = isActive,
        };

    [Fact]
    public void Plan_SchedulesOneReminderPerSubscription_AtItsLeadTime()
    {
        var plan = RenewalNotificationPlanner.Plan([Subscription("Netflix", daysUntilBilling: 10, alertDaysAdvance: 3)], Now);

        var notification = Assert.Single(plan);
        // 10 days out, remind 3 days before -> 7 days from now, at 09:00.
        Assert.Equal(new DateTime(2026, 8, 14, 9, 0, 0), notification.NotifyAt);
        Assert.Equal("Subscription renewing soon", notification.Title);
        Assert.Equal("Netflix renews in 3 days - $15.99", notification.Body);
    }

    [Fact]
    public void Plan_UsesPaymentCopyForOneTimePurchases()
    {
        var plan = RenewalNotificationPlanner.Plan(
            [Subscription("Domain renewal", daysUntilBilling: 10, cadence: BillingCycleType.OneTime)],
            Now);

        var notification = Assert.Single(plan);
        Assert.Equal("Payment due soon", notification.Title);
        Assert.StartsWith("Domain renewal is due in 3 days - ", notification.Body);
    }

    [Fact]
    public void Plan_SkipsInactiveSubscriptions()
    {
        var plan = RenewalNotificationPlanner.Plan(
            [Subscription("Cancelled Thing", daysUntilBilling: 10, isActive: false)],
            Now);

        // A deactivated subscription is not being charged for, so reminding about it is noise.
        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_SkipsRemindersWhoseMomentHasPassed()
    {
        // Billing is in 1 day but the lead time is 7, so the reminder moment was 6 days ago. The OS
        // would either fire it instantly or reject it; both are wrong once the charge is imminent.
        var plan = RenewalNotificationPlanner.Plan(
            [Subscription("Already Due", daysUntilBilling: 1, alertDaysAdvance: 7)],
            Now);

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_SkipsTodaysReminderOnceItsTimeOfDayHasGone()
    {
        // Reminder time is 09:00; "now" in these tests is 10:00, so today's slot is already gone.
        var plan = RenewalNotificationPlanner.Plan(
            [Subscription("Today", daysUntilBilling: 3, alertDaysAdvance: 3)],
            Now);

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_OrdersBySoonestFirst()
    {
        var plan = RenewalNotificationPlanner.Plan(
        [
            Subscription("Later", daysUntilBilling: 40),
            Subscription("Sooner", daysUntilBilling: 10),
            Subscription("Middle", daysUntilBilling: 20),
        ], Now);

        Assert.Equal(["Sooner", "Middle", "Later"], plan.Select(n => n.Body.Split(' ')[0]).ToArray());
    }

    [Fact]
    public void Plan_CapsAtThePlatformLimit_KeepingTheNearestDates()
    {
        // iOS holds at most 64 pending local notifications and silently drops the rest, so the ones
        // that survive must be the soonest - not whichever order the list happened to arrive in.
        var subscriptions = Enumerable.Range(1, 100)
            .Select(i => Subscription($"Sub{i:D3}", daysUntilBilling: 200 - i))
            .ToList();

        var plan = RenewalNotificationPlanner.Plan(subscriptions, Now);

        Assert.Equal(RenewalNotificationPlanner.MaxPendingNotifications, plan.Count);
        Assert.True(plan.SequenceEqual(plan.OrderBy(n => n.NotifyAt)));
        // Sub100 bills soonest (100 days out); Sub001 is furthest and must be the one dropped.
        Assert.Contains(plan, n => n.Body.StartsWith("Sub100"));
        Assert.DoesNotContain(plan, n => n.Body.StartsWith("Sub001"));
    }

    [Fact]
    public void Plan_AssignsIdsThatAreUniqueWithinTheBatch()
    {
        var plan = RenewalNotificationPlanner.Plan(
            Enumerable.Range(1, 20).Select(i => Subscription($"Sub{i}", daysUntilBilling: i + 10)).ToList(),
            Now);

        Assert.Equal(plan.Count, plan.Select(n => n.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(0, "renews today")]
    [InlineData(1, "renews tomorrow")]
    [InlineData(3, "renews in 3 days")]
    [InlineData(7, "renews in 7 days")]
    public void Plan_PhrasesTheLeadTimeNaturally(int alertDaysAdvance, string expectedPhrase)
    {
        var plan = RenewalNotificationPlanner.Plan(
            [Subscription("Netflix", daysUntilBilling: 30, alertDaysAdvance: alertDaysAdvance)],
            Now);

        Assert.Contains(expectedPhrase, Assert.Single(plan).Body);
    }

    [Fact]
    public void Plan_IncludesTheAmount_SoTheReminderIsActionable()
    {
        var plan = RenewalNotificationPlanner.Plan(
            [Subscription("Adobe Creative Cloud", daysUntilBilling: 30, cost: 1699.50m)],
            Now);

        // "renews in 3 days" is a fact; the amount is what turns it into a decision.
        Assert.Contains("$1,699.50", Assert.Single(plan).Body);
    }

    [Fact]
    public void Plan_WithNoSubscriptions_SchedulesNothing()
    {
        Assert.Empty(RenewalNotificationPlanner.Plan([], Now));
    }
}
