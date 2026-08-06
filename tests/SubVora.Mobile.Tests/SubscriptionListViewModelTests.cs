using SubVora.Mobile.Api.Dtos;
using CommunityToolkit.Mvvm.Messaging;
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
        FakeUserPrompt? userPrompt = null,
        IMessenger? messenger = null,
        FakeRenewalNotificationScheduler? notificationScheduler = null) =>
        new(
            api ?? new FakeSubscriptionsApi(),
            cache ?? new FakeLocalCacheService(),
            userPrompt ?? new FakeUserPrompt(),
            messenger ?? new WeakReferenceMessenger(),
            notificationScheduler ?? new FakeRenewalNotificationScheduler());

    [Fact]
    public async Task LoadAsync_PreservesLogoUrlAndFreeTrialFlagForTheItemTemplate()
    {
        // The list template binds CatalogLogoUrl (logo, with the placeholder showing through when
        // it is null/empty) and IsFreeTrial (badge) directly, so both must survive load untouched -
        // in particular a null logo must stay null rather than being coerced to "".
        var withLogo = SampleSubscription("Netflix");
        withLogo.CatalogLogoUrl = "https://cdn.example.com/netflix.svg";
        withLogo.IsFreeTrial = true;
        var withoutLogo = SampleSubscription("Local Gym");

        var api = new FakeSubscriptionsApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([withLogo, withoutLogo]) };
        var viewModel = CreateViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("https://cdn.example.com/netflix.svg", viewModel.Subscriptions[0].CatalogLogoUrl);
        Assert.True(viewModel.Subscriptions[0].IsFreeTrial);
        Assert.Null(viewModel.Subscriptions[1].CatalogLogoUrl);
        Assert.False(viewModel.Subscriptions[1].IsFreeTrial);
    }

    [Fact]
    public async Task CachedList_RoundTripsLogoUrlAndFreeTrialFlag()
    {
        // Offline the list is served from the SQLite mirror, and must render identically.
        var subscription = SampleSubscription("Spotify");
        subscription.CatalogLogoUrl = "https://cdn.example.com/spotify.svg";
        subscription.IsFreeTrial = true;
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(CachedSubscription.FromDto(subscription));

        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw new HttpRequestException("network down") };
        var viewModel = CreateViewModel(api, cache);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsShowingCachedData);
        Assert.Equal("https://cdn.example.com/spotify.svg", viewModel.Subscriptions[0].CatalogLogoUrl);
        Assert.True(viewModel.Subscriptions[0].IsFreeTrial);
    }

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

    [Fact]
    public async Task LoadAsync_ReschedulesRemindersFromTheLoadedList()
    {
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([SampleSubscription("Netflix")]),
        };
        var scheduler = new FakeRenewalNotificationScheduler();
        var viewModel = CreateViewModel(api, notificationScheduler: scheduler);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var scheduled = Assert.Single(scheduler.SyncCalls);
        Assert.Equal("Netflix", Assert.Single(scheduled).CustomName);
    }

    [Fact]
    public async Task LoadAsync_FallingBackToCache_StillSchedulesFromTheCachedList()
    {
        // Offline is exactly when reminders matter most - the schedule must not go empty because
        // the network did.
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(CachedSubscription.FromDto(SampleSubscription("Spotify")));
        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw new HttpRequestException("offline") };
        var scheduler = new FakeRenewalNotificationScheduler();
        var viewModel = CreateViewModel(api, cache, notificationScheduler: scheduler);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsShowingCachedData);
        var scheduled = Assert.Single(scheduler.SyncCalls);
        Assert.Equal("Spotify", Assert.Single(scheduled).CustomName);
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_ReschedulesWithoutTheDeletedSubscription()
    {
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>(
                [SampleSubscription("Netflix"), SampleSubscription("Spotify")]),
        };
        var scheduler = new FakeRenewalNotificationScheduler();
        var viewModel = CreateViewModel(api, userPrompt: new FakeUserPrompt { ConfirmResult = true }, notificationScheduler: scheduler);
        await viewModel.LoadCommand.ExecuteAsync(null);
        var doomed = viewModel.Subscriptions.Single(s => s.CustomName == "Netflix").Id;

        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(doomed);

        // A cancelled subscription must stop reminding, which only happens if the delete path
        // reschedules rather than leaving the previous set pending.
        Assert.Equal(2, scheduler.SyncCalls.Count);
        Assert.Equal("Spotify", Assert.Single(scheduler.SyncCalls[^1]).CustomName);
    }
}
