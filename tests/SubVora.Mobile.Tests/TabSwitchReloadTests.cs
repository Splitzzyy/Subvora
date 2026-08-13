using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Models;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Shell raises OnAppearing on every tab selection, so a page that loads unconditionally there
/// refetches on each tab tap - which against a slow or unreachable API is a spinner every time the
/// user comes back to a screen, and reads as an app stuck refreshing itself.
/// <para>
/// Each page's OnAppearing calls EnsureLoadedCommand instead. These pin the two halves of that:
/// a repeat appearance costs nothing, and the things that genuinely invalidate a screen - a write
/// somewhere else, a sign-out, an empty failed load - still make the next visit fetch.
/// </para>
/// </summary>
public class TabSwitchReloadTests
{
    private static SubscriptionDto Subscription(string name = "Netflix") => new()
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

    private static (FakeSubscriptionsApi Api, SubscriptionListViewModel ViewModel) ListWith(IMessenger messenger)
    {
        var api = new FakeSubscriptionsApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([Subscription()]),
        };

        return (api, new SubscriptionListViewModel(
            api,
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            messenger,
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService()));
    }

    private static int GetAllCalls(FakeSubscriptionsApi api) => api.GetAllCallCount;

    [Fact]
    public async Task SubscriptionList_SecondAppearance_DoesNotRefetch()
    {
        var (api, viewModel) = ListWith(new WeakReferenceMessenger());

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Equal(1, GetAllCalls(api));
        Assert.Single(viewModel.Subscriptions);
    }

    [Fact]
    public async Task SubscriptionList_PullToRefresh_StillFetchesEveryTime()
    {
        // The gate is on appearing, not on the user asking. LoadCommand is what RefreshView binds.
        var (api, viewModel) = ListWith(new WeakReferenceMessenger());

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, GetAllCalls(api));
    }

    [Fact]
    public async Task SubscriptionList_AfterAChangeElsewhere_RefetchesOnTheNextAppearance()
    {
        var messenger = new WeakReferenceMessenger();
        var (api, viewModel) = ListWith(messenger);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        // What the detail screen publishes after a save or a delete.
        messenger.Send(new SubscriptionsChangedMessage());
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Equal(2, GetAllCalls(api));
    }

    [Fact]
    public async Task SubscriptionList_AfterSignOut_ForgetsTheRowsAndFetchesAgain()
    {
        // Shell keeps the page it built for each tab, so without this the next user to sign in
        // would be shown the previous one's subscriptions and no request would follow.
        var messenger = new WeakReferenceMessenger();
        var (api, viewModel) = ListWith(messenger);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        messenger.Send(new SessionEndedMessage());

        Assert.Empty(viewModel.Subscriptions);
        Assert.Empty(viewModel.Groups);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(2, GetAllCalls(api));
    }

    [Fact]
    public async Task SubscriptionList_ServedFromCache_CountsAsLoaded()
    {
        // Offline with a mirror to show is exactly the case the refetch made painful: every tab tap
        // spent the full request timeout to end up showing the same cached rows again.
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(CachedSubscription.FromDto(Subscription()));

        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw TestApiExceptions.ConnectionFailure() };
        var viewModel = new SubscriptionListViewModel(
            api,
            cache,
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService());

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsShowingCachedData);
        Assert.Single(viewModel.Subscriptions);
        Assert.Equal(1, GetAllCalls(api));
    }

    [Fact]
    public async Task SubscriptionList_FailedWithNothingToShow_RetriesOnTheNextAppearance()
    {
        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw TestApiExceptions.ConnectionFailure() };
        var viewModel = new SubscriptionListViewModel(
            api,
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService());

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        // An empty error screen is not a loaded screen - coming back to the tab is the retry.
        Assert.Equal(2, GetAllCalls(api));
    }

    [Fact]
    public async Task Dashboard_SecondAppearance_DoesNotRefetch()
    {
        var calls = 0;
        var api = new FakeDashboardApi
        {
            Handler = () =>
            {
                calls++;
                return Task.FromResult(new BurnRateResult { Weekly = 1m, Monthly = 4m, Yearly = 50m, HomeCurrency = "USD" });
            },
        };
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Dashboard_AfterSignOut_FetchesAgainForTheNextSession()
    {
        var calls = 0;
        var api = new FakeDashboardApi
        {
            Handler = () =>
            {
                calls++;
                return Task.FromResult(new BurnRateResult { Weekly = 1m, Monthly = 4m, Yearly = 50m, HomeCurrency = "USD" });
            },
        };
        var messenger = new WeakReferenceMessenger();

        // Singleton for the whole app, so it is the one view model that survives sign-out for sure.
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), messenger);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        messenger.Send(new SessionEndedMessage());
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Categories_SecondAppearance_DoesNotRefetch()
    {
        var calls = 0;
        var api = new FakeCategoriesApi
        {
            GetAllHandler = () =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<CategoryDto>>(
                    [new CategoryDto { Id = Guid.NewGuid(), Name = "Music", IsSystemDefault = false }]);
            },
        };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new CategoriesViewModel(api, new FakeConnectivityService(), new FakeUserPrompt(), messenger);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);

        messenger.Send(new SessionEndedMessage());
        Assert.Empty(viewModel.Categories);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PaymentSources_SecondAppearance_DoesNotRefetch()
    {
        var calls = 0;
        var api = new FakePaymentSourcesApi
        {
            GetAllHandler = () =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<PaymentSourceDto>>(
                    [new PaymentSourceDto { Id = Guid.NewGuid(), Label = "HDFC Card", SourceType = PaymentSourceType.Card }]);
            },
        };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new PaymentSourcesViewModel(api, new FakeUserPrompt(), new FakeConnectivityService(), messenger);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);

        messenger.Send(new SessionEndedMessage());
        Assert.Empty(viewModel.PaymentSources);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Settings_SecondAppearance_DoesNotRefetchTheProfile()
    {
        var calls = 0;
        var usersApi = new FakeUsersApi
        {
            GetMeHandler = () =>
            {
                calls++;
                return Task.FromResult(new UserProfileDto
                {
                    Id = Guid.NewGuid(),
                    Email = "user@example.com",
                    PreferredCurrency = "INR",
                    DefaultAlertDaysAdvance = 3,
                });
            },
        };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new SettingsViewModel(
            usersApi,
            new FakeAuthApi(),
            new FakeTokenStore(),
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            messenger,
            new FakeThemeService(),
            new FakeConnectivityService());

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);

        messenger.Send(new SessionEndedMessage());
        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RenamingACategory_TellsTheOtherScreens()
    {
        // The dashboard names categories in its breakdown and the list groups by them. Neither
        // refetches on tab switch any more, so the rename has to publish or they show the old name.
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Music", IsSystemDefault = false };
        var api = new FakeCategoriesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>([category]),
        };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new CategoriesViewModel(
            api,
            new FakeConnectivityService(),
            new FakeUserPrompt { PromptResult = "Podcasts" },
            messenger);

        var announced = 0;
        var listener = new object();
        messenger.Register<SubscriptionsChangedMessage>(listener, (_, _) => announced++);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.RenameCommand.ExecuteAsync(viewModel.Categories[0]);

        Assert.Equal(1, announced);
        GC.KeepAlive(listener);
    }

    [Fact]
    public async Task RenamingAPaymentSource_TellsTheOtherScreens()
    {
        var source = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "HDFC Card", SourceType = PaymentSourceType.Card };
        var api = new FakePaymentSourcesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([source]),
        };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new PaymentSourcesViewModel(
            api,
            new FakeUserPrompt { PromptResult = "ICICI Card" },
            new FakeConnectivityService(),
            messenger);

        var announced = 0;
        var listener = new object();
        messenger.Register<SubscriptionsChangedMessage>(listener, (_, _) => announced++);

        await viewModel.EnsureLoadedCommand.ExecuteAsync(null);
        await viewModel.RenameCommand.ExecuteAsync(viewModel.PaymentSources[0]);

        Assert.Equal(1, announced);
        GC.KeepAlive(listener);
    }
}
