using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The always-visible banner in AppShell binds to <see cref="DashboardViewModel.Summary"/> and is
/// refreshed by messages the mutation view models publish. These cover that contract; the XAML
/// binding itself is not unit-testable without a MAUI host.
/// </summary>
public class BurnRateBannerTests
{
    private static BurnRateResult BurnRate(decimal weekly, decimal monthly, decimal yearly, string currency = "USD") =>
        new() { Weekly = weekly, Monthly = monthly, Yearly = yearly, HomeCurrency = currency };

    [Fact]
    public async Task Summary_AfterLoad_ReadsAsAOneLineSpendStatement()
    {
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(BurnRate(12.5m, 54.17m, 650m)) };
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("USD 12.50/wk | 54.17/mo | 650.00/yr", viewModel.Summary);
    }

    [Fact]
    public void Summary_BeforeAnyLoad_IsEmptySoTheBannerStaysHidden()
    {
        var viewModel = new DashboardViewModel(new FakeDashboardApi(), new FakeLocalCacheService(), new WeakReferenceMessenger());

        // The XAML binds IsVisible to this same string via IsStringNotNullOrEmptyConverter, so
        // "empty" is what keeps the strip off screen for a signed-out user.
        Assert.Equal(string.Empty, viewModel.Summary);
    }

    [Fact]
    public async Task Summary_RaisesPropertyChanged_WhenTheFiguresMove()
    {
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(BurnRate(1m, 4m, 50m)) };
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), new WeakReferenceMessenger());

        var raised = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.Summary))
            {
                raised++;
            }
        };

        await viewModel.LoadCommand.ExecuteAsync(null);

        // Without the notification the banner would keep rendering the previous figures.
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task SubscriptionsChangedMessage_RefetchesTheBurnRate()
    {
        var calls = 0;
        var api = new FakeDashboardApi
        {
            Handler = () =>
            {
                calls++;
                return Task.FromResult(BurnRate(calls * 10m, calls * 40m, calls * 500m));
            },
        };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), messenger);

        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Equal("USD 10.00/wk | 40.00/mo | 500.00/yr", viewModel.Summary);

        messenger.Send(new SubscriptionsChangedMessage());

        Assert.Equal(2, calls);
        Assert.Equal("USD 20.00/wk | 80.00/mo | 1,000.00/yr", viewModel.Summary);
    }

    [Fact]
    public async Task SessionEndedMessage_ClearsTheFiguresSoTheyDoNotOutliveTheSession()
    {
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(BurnRate(12.5m, 54.17m, 650m)) };
        var messenger = new WeakReferenceMessenger();
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), messenger);

        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.NotEqual(string.Empty, viewModel.Summary);

        messenger.Send(new SessionEndedMessage());

        Assert.Equal(string.Empty, viewModel.Summary);
        Assert.Equal(0m, viewModel.Weekly);
        Assert.Equal(string.Empty, viewModel.HomeCurrency);
        Assert.Empty(viewModel.ByCategory);
    }

    [Fact]
    public async Task SavingASubscription_PublishesTheRefreshMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var received = 0;
        messenger.Register<SubscriptionsChangedMessage>(this, (_, _) => received++);

        var viewModel = new SubscriptionDetailViewModel(
            new FakeSubscriptionsApi(),
            new FakeCategoriesApi(),
            new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            messenger,
            new FakeUserPrompt(),
            new FakeConnectivityService())
        {
            CustomName = "Netflix",
            CostAmount = 15.99m,
            Currency = "USD",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task AFailedSave_PublishesNothing()
    {
        var messenger = new WeakReferenceMessenger();
        var received = 0;
        messenger.Register<SubscriptionsChangedMessage>(this, (_, _) => received++);

        var api = new FakeSubscriptionsApi
        {
            CreateHandler = _ => throw TestApiExceptions.Create(System.Net.HttpStatusCode.BadRequest),
        };
        var viewModel = new SubscriptionDetailViewModel(
            api,
            new FakeCategoriesApi(),
            new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            messenger,
            new FakeUserPrompt(),
            new FakeConnectivityService())
        {
            CustomName = "Netflix",
            CostAmount = 15.99m,
            Currency = "USD",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        // The headline figure must not move for a subscription the server rejected.
        Assert.Equal(0, received);
        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task DeletingASubscription_PublishesTheRefreshMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var received = 0;
        messenger.Register<SubscriptionsChangedMessage>(this, (_, _) => received++);

        var api = new FakeSubscriptionsApi();
        var viewModel = new SubscriptionListViewModel(
            api,
            new FakeLocalCacheService(),
            new FakeUserPrompt { ConfirmResult = true },
            messenger,
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService());

        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task ChangingTheHomeCurrency_PublishesTheRefreshMessage()
    {
        // Every burn-rate figure is converted into the home currency, so switching it changes the
        // headline numbers without a single subscription being edited.
        var messenger = new WeakReferenceMessenger();
        var received = 0;
        messenger.Register<SubscriptionsChangedMessage>(this, (_, _) => received++);

        var viewModel = new SettingsViewModel(
            new FakeUsersApi(),
            new FakeAccountApi(),
            new FakeTokenStore(),
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            messenger,
            new FakeThemeService(),
            new FakeConnectivityService())
        {
            PreferredCurrency = "EUR",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task SigningOut_PublishesTheSessionEndedMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var received = 0;
        messenger.Register<SessionEndedMessage>(this, (_, _) => received++);

        var viewModel = new SettingsViewModel(
            new FakeUsersApi(),
            new FakeAccountApi(),
            new FakeTokenStore(),
            new FakeLocalCacheService(),
            new FakeUserPrompt { ConfirmResult = true },
            messenger,
            new FakeThemeService(),
            new FakeConnectivityService());

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(1, received);
    }
}
