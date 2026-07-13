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

    private static SubscriptionListViewModel CreateViewModel(
        FakeSubscriptionsApi? api = null,
        FakeLocalCacheService? cache = null,
        FakeUserPrompt? userPrompt = null) =>
        new(api ?? new FakeSubscriptionsApi(), cache ?? new FakeLocalCacheService(), userPrompt ?? new FakeUserPrompt());

    [Fact]
    public async Task LoadAsync_OnSuccess_PopulatesListAndUpsertsCache()
    {
        var subscription = SampleSubscription();
        var api = new FakeSubscriptionsApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([subscription]) };
        var cache = new FakeLocalCacheService();
        var viewModel = CreateViewModel(api, cache);

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
        var viewModel = CreateViewModel(api, cache);

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
        var viewModel = CreateViewModel(api, cache);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Subscriptions);
        Assert.False(viewModel.IsShowingCachedData);
        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_WhenConfirmed_CallsDeleteAndRemovesFromListAndCache()
    {
        var subscription = SampleSubscription();
        var api = new FakeSubscriptionsApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([subscription]) };
        var cache = new FakeLocalCacheService();
        var userPrompt = new FakeUserPrompt { ConfirmResult = true };
        var viewModel = CreateViewModel(api, cache, userPrompt);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(subscription.Id);

        Assert.Empty(viewModel.Subscriptions);
        Assert.Single(api.DeleteCalls);
        Assert.Equal(subscription.Id, api.DeleteCalls[0]);

        var cached = await cache.GetAllAsync<CachedSubscription>();
        Assert.Empty(cached);
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_WhenDeclined_MakesNoApiCallAndLeavesItemInPlace()
    {
        var subscription = SampleSubscription();
        var api = new FakeSubscriptionsApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([subscription]) };
        var userPrompt = new FakeUserPrompt { ConfirmResult = false };
        var viewModel = CreateViewModel(api, userPrompt: userPrompt);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(subscription.Id);

        Assert.Single(viewModel.Subscriptions);
        Assert.Empty(api.DeleteCalls);
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_OnApiFailure_ShowsErrorAndLeavesItemInList()
    {
        var subscription = SampleSubscription();
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([subscription]),
            DeleteHandler = _ => throw new HttpRequestException("network down"),
        };
        var userPrompt = new FakeUserPrompt { ConfirmResult = true };
        var viewModel = CreateViewModel(api, userPrompt: userPrompt);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(subscription.Id);

        Assert.Single(viewModel.Subscriptions);
        Assert.NotNull(viewModel.ErrorMessage);
    }
}
