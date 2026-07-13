using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class SubscriptionListViewModelTests
{
    private static SubscriptionDto SampleSubscription(string name = "Netflix") => new()
    {
        Id = Guid.NewGuid(),
        CustomName = name,
        CostAmount = 15.99m,
        Currency = "USD",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = new DateOnly(2026, 8, 1),
        AlertDaysAdvance = 3,
        CategoryName = "Streaming",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task LoadAsync_OnSuccess_PopulatesListAndUpsertsCache()
    {
        var subscription = SampleSubscription();
        var api = new FakeSubscriptionsApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([subscription]) };
        var cache = new FakeLocalCacheService();
        var viewModel = new SubscriptionListViewModel(api, cache);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Subscriptions);
        Assert.Equal("Netflix", viewModel.Subscriptions[0].CustomName);
        Assert.False(viewModel.IsShowingCachedData);
        Assert.Null(viewModel.ErrorMessage);

        var cached = await cache.GetAllAsync<CachedSubscription>();
        Assert.Single(cached);
        Assert.Equal(subscription.Id, cached[0].Id);
    }

    [Fact]
    public async Task LoadAsync_OnApiFailureWithPopulatedCache_FallsBackToCachedItemsWithOfflineIndicator()
    {
        var subscription = SampleSubscription("Spotify");
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(CachedSubscription.FromDto(subscription));

        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw new HttpRequestException("network down") };
        var viewModel = new SubscriptionListViewModel(api, cache);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Subscriptions);
        Assert.Equal("Spotify", viewModel.Subscriptions[0].CustomName);
        Assert.True(viewModel.IsShowingCachedData);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_OnApiFailureWithEmptyCache_ShowsEmptyErrorState()
    {
        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw new HttpRequestException("network down") };
        var cache = new FakeLocalCacheService();
        var viewModel = new SubscriptionListViewModel(api, cache);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Subscriptions);
        Assert.False(viewModel.IsShowingCachedData);
        Assert.NotNull(viewModel.ErrorMessage);
    }
}
