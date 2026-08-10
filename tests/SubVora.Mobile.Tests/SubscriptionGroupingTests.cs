using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The subscriptions list groups by category and orders everything by billing date, so the next
/// thing to be charged is at the top. These pin that ordering - the whole point of the screen.
/// </summary>
public class SubscriptionGroupingTests
{
    private static readonly DateOnly Today = new(2026, 8, 7);

    private static SubscriptionDto Sub(string name, string? category, int daysUntilBilling) => new()
    {
        Id = Guid.NewGuid(),
        CustomName = name,
        CategoryName = category,
        CostAmount = 10m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        NextBillingDate = Today.AddDays(daysUntilBilling),
        IsActive = true,
    };

    private static async Task<SubscriptionListViewModel> LoadAsync(params SubscriptionDto[] subscriptions)
    {
        var viewModel = new SubscriptionListViewModel(
            new FakeSubscriptionsApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>(subscriptions) },
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(),
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService());

        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task GroupsAreOrderedByWhicheverCategoryBillsSoonest()
    {
        var viewModel = await LoadAsync(
            Sub("Netflix", "Entertainment", 20),
            Sub("Gym", "Fitness", 2),
            Sub("Notion", "Productivity", 9));

        Assert.Equal(["Fitness", "Productivity", "Entertainment"], viewModel.Groups.Select(g => g.CategoryName));
    }

    [Fact]
    public async Task RowsInsideAGroupAreOrderedByBillingDate()
    {
        var viewModel = await LoadAsync(
            Sub("Prime", "Entertainment", 25),
            Sub("Netflix", "Entertainment", 3),
            Sub("Spotify", "Entertainment", 11));

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal(["Netflix", "Spotify", "Prime"], group.Select(s => s.CustomName));
    }

    [Fact]
    public async Task SubscriptionsWithoutACategoryAreStillListed()
    {
        var viewModel = await LoadAsync(Sub("Some SaaS", null, 4), Sub("Blank", "   ", 6));

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal(SubscriptionGroup.UncategorisedName, group.CategoryName);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public async Task TheFlatListStaysInServerOrderSoTheSchedulerAndCacheAreUnaffected()
    {
        var viewModel = await LoadAsync(
            Sub("Prime", "Entertainment", 25),
            Sub("Gym", "Fitness", 2));

        Assert.Equal(["Prime", "Gym"], viewModel.Subscriptions.Select(s => s.CustomName));
    }

    [Fact]
    public async Task DeletingTheLastRowOfACategoryDropsItsGroup()
    {
        var viewModel = await LoadAsync(
            Sub("Netflix", "Entertainment", 20),
            Sub("Gym", "Fitness", 2));

        var gymId = viewModel.Subscriptions.Single(s => s.CustomName == "Gym").Id;
        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(gymId);

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal("Entertainment", group.CategoryName);
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(5, "in 5 days")]
    [InlineData(14, "in 14 days")]
    public void NearDatesReadAsRelative(int daysAway, string expected)
    {
        Assert.Equal(expected, RelativeDate.Describe(Today.AddDays(daysAway), Today));
    }

    [Fact]
    public void DistantDatesFallBackToACalendarDate()
    {
        // "in 63 days" is harder to place than a date, so past a fortnight it switches.
        Assert.Equal("9 Oct", RelativeDate.Describe(new DateOnly(2026, 10, 9), Today));
    }

    [Fact]
    public void PastDatesSayOverdueRatherThanCountingBackwards()
    {
        Assert.Equal("overdue since 1 Aug", RelativeDate.Describe(new DateOnly(2026, 8, 1), Today));
    }
}
