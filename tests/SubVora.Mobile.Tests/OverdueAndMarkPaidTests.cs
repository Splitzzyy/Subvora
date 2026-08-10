using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Nothing moves a billing date on a timer any more, so a date in the past means the charge is
/// genuinely outstanding. These pin that reading and the mark-paid round trip.
/// </summary>
public class OverdueAndMarkPaidTests
{
    private static SubscriptionDto Sub(string name, int daysUntilBilling, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        CustomName = name,
        CategoryName = "Entertainment",
        CostAmount = 10m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        NextBillingDate = DateOnly.FromDateTime(DateTime.Today).AddDays(daysUntilBilling),
        IsActive = isActive,
    };

    private static SubscriptionListViewModel CreateViewModel(FakeSubscriptionsApi api, IMessenger? messenger = null) =>
        new(
            api,
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            messenger ?? new WeakReferenceMessenger(),
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService());

    [Fact]
    public void APassedBillingDateIsOverdue()
    {
        Assert.True(Sub("Netflix", -1).IsOverdue);
    }

    [Fact]
    public void TodayIsNotYetOverdue()
    {
        // Due today is due, not late - the charge still has the rest of the day to land.
        Assert.False(Sub("Netflix", 0).IsOverdue);
    }

    [Fact]
    public void AFutureBillingDateIsNotOverdue()
    {
        Assert.False(Sub("Netflix", 5).IsOverdue);
    }

    [Fact]
    public void AnInactiveSubscriptionIsNeverOverdue()
    {
        // A cancelled or completed one-time subscription owes nothing, however old its date is.
        Assert.False(Sub("Old thing", -400, isActive: false).IsOverdue);
    }

    [Fact]
    public async Task GroupHeaderLeadsWithTheOverdueCountRatherThanTheNextCharge()
    {
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>(
                [Sub("Netflix", -3), Sub("Spotify", 10)]),
        };
        var viewModel = CreateViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal(1, group.OverdueCount);
        Assert.Contains("1 overdue", group.Summary);
    }

    [Fact]
    public async Task GroupHeaderFallsBackToTheNextChargeWhenNothingIsOwed()
    {
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([Sub("Spotify", 3)]),
        };
        var viewModel = CreateViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal(0, group.OverdueCount);
        Assert.Contains("next in 3 days", group.Summary);
    }

    [Fact]
    public async Task MarkingPaidReplacesTheRowWithWhateverTheServerReturned()
    {
        var overdue = Sub("Netflix", -3);
        var settled = new SubscriptionDto
        {
            Id = overdue.Id,
            CustomName = "Netflix",
            CategoryName = "Entertainment",
            Currency = "INR",
            CostAmount = 10m,
            CycleCadence = BillingCycleType.Monthly,
            NextBillingDate = overdue.NextBillingDate.AddMonths(1),
            LastPaidDate = overdue.NextBillingDate,
            IsActive = true,
        };

        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([overdue]),
            MarkPaidHandler = _ => Task.FromResult(settled),
        };
        var viewModel = CreateViewModel(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkPaidCommand.ExecuteAsync(overdue.Id);

        Assert.Equal([overdue.Id], api.MarkPaidCalls);
        // The new date comes from the server, which advances one cycle from the date just settled -
        // not from today, which would silently forgive the periods in between.
        var row = Assert.Single(viewModel.Subscriptions);
        Assert.Equal(settled.NextBillingDate, row.NextBillingDate);
        Assert.Equal(overdue.NextBillingDate, row.LastPaidDate);
    }

    [Fact]
    public async Task MarkingPaidFailing_SurfacesAnErrorAndLeavesTheRowAlone()
    {
        var overdue = Sub("Netflix", -3);
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([overdue]),
            MarkPaidHandler = _ => throw TestApiExceptions.Create(System.Net.HttpStatusCode.InternalServerError),
        };
        var viewModel = CreateViewModel(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.MarkPaidCommand.ExecuteAsync(overdue.Id);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.True(Assert.Single(viewModel.Subscriptions).IsOverdue);
    }
}
