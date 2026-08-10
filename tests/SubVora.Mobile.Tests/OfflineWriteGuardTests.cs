using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Writes are not queued offline - the SQLite mirror is refreshed from successful GETs only and
/// there is no outbox. So the app has to say so, and stop offering a button that cannot work.
/// </summary>
public class OfflineWriteGuardTests
{
    private const string OfflineWriteMessage = "You're offline — this change wasn't saved. Try again once you're connected.";

    private static SettingsViewModel Settings(FakeConnectivityService connectivity, FakeUsersApi? usersApi = null) =>
        new(
            usersApi ?? new FakeUsersApi(),
            new FakeAuthApi(),
            new FakeTokenStore(),
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            new FakeThemeService(),
            connectivity);

    private static SubscriptionDetailViewModel Detail(FakeConnectivityService connectivity, FakeSubscriptionsApi? subscriptionsApi = null) =>
        new(
            subscriptionsApi ?? new FakeSubscriptionsApi(),
            new FakeCategoriesApi(),
            new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            new WeakReferenceMessenger(),
            new FakeUserPrompt(),
            connectivity);

    private static SubscriptionListViewModel List(FakeConnectivityService connectivity, FakeSubscriptionsApi? subscriptionsApi = null) =>
        new(
            subscriptionsApi ?? new FakeSubscriptionsApi(),
            new FakeLocalCacheService(),
            new FakeUserPrompt { ConfirmResult = true },
            new WeakReferenceMessenger(),
            new FakeRenewalNotificationScheduler(),
            connectivity);

    [Fact]
    public void WithNoNetwork_TheWriteButtonIsNotOffered()
    {
        var offline = new FakeConnectivityService { IsConnected = false };

        Assert.False(Settings(offline).CanSubmit);
        Assert.False(Detail(offline).CanSubmit);
        // The list serves itself from the SQLite mirror, so it is the screen most likely to be open
        // with no network - and it carries two writes, swipe-to-delete and mark-as-paid.
        Assert.False(List(offline).CanSubmit);
        Assert.False(new CategoriesViewModel(new FakeCategoriesApi(), offline, new FakeUserPrompt()).CanSubmit);
        Assert.False(new PaymentSourcesViewModel(new FakePaymentSourcesApi(), new FakeUserPrompt(), offline).CanSubmit);
    }

    [Fact]
    public void WithNetwork_TheWriteButtonIsOffered()
    {
        var online = new FakeConnectivityService { IsConnected = true };

        Assert.True(Settings(online).CanSubmit);
        Assert.True(Detail(online).CanSubmit);
        Assert.True(List(online).CanSubmit);
        Assert.True(new CategoriesViewModel(new FakeCategoriesApi(), online, new FakeUserPrompt()).CanSubmit);
        Assert.True(new PaymentSourcesViewModel(new FakePaymentSourcesApi(), new FakeUserPrompt(), online).CanSubmit);
    }

    [Fact]
    public async Task AFailedMarkPaid_SaysTheChargeWasNotSettled()
    {
        // The worst of the offline writes to get wrong: the row keeps its OVERDUE chip either way,
        // so "you appear to be offline" reads as "we'll sync it" while the charge stays unsettled.
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            MarkPaidHandler = _ => throw new HttpRequestException("Connection refused"),
        };
        var viewModel = List(new FakeConnectivityService { IsConnected = false }, subscriptionsApi);

        await viewModel.MarkPaidCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(OfflineWriteMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AFailedSwipeDelete_AlsoSaysTheChangeDidNotLand()
    {
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            DeleteHandler = _ => throw new HttpRequestException("Connection refused"),
        };
        var viewModel = List(new FakeConnectivityService { IsConnected = false }, subscriptionsApi);

        await viewModel.DeleteSubscriptionCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(OfflineWriteMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task TheListClosesItsWriteButtonsWhenAWriteRevealsTheConnectionIsGone()
    {
        // Same re-read-on-failure rule the other screens follow: the connection can drop while the
        // list is already on screen, and only a failed write finds out.
        var connectivity = new FakeConnectivityService { IsConnected = true };
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            MarkPaidHandler = _ => throw new HttpRequestException("Connection refused"),
        };
        var viewModel = List(connectivity, subscriptionsApi);
        Assert.True(viewModel.CanSubmit);

        connectivity.IsConnected = false;
        await viewModel.MarkPaidCommand.ExecuteAsync(Guid.NewGuid());

        Assert.True(viewModel.IsOffline);
        Assert.False(viewModel.CanSubmit);
    }

    [Fact]
    public void AWriteInFlightAlsoClosesTheButton()
    {
        // CanSubmit folds IsBusy in as well, so a second tap cannot land while the first is running.
        var viewModel = Settings(new FakeConnectivityService { IsConnected = true });

        Assert.True(viewModel.CanSubmit);
        viewModel.IsBusy = true;
        Assert.False(viewModel.CanSubmit);
    }

    [Fact]
    public async Task AFailedSave_SaysTheChangeWasNotSaved()
    {
        // The distinction that matters: "you appear to be offline" leaves the reader guessing
        // whether it will sync later. It will not.
        var usersApi = new FakeUsersApi { UpdateMeHandler = _ => throw new HttpRequestException("Connection refused") };
        var viewModel = Settings(new FakeConnectivityService { IsConnected = false }, usersApi);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(OfflineWriteMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AFailedSave_MarksTheScreenOfflineSoTheButtonCloses()
    {
        // The connection can drop while the screen is open - state is re-read on failure rather
        // than subscribed to, because these view models are transient and the service is a
        // singleton.
        var connectivity = new FakeConnectivityService { IsConnected = true };
        var usersApi = new FakeUsersApi { UpdateMeHandler = _ => throw new HttpRequestException("Connection refused") };
        var viewModel = Settings(connectivity, usersApi);
        Assert.True(viewModel.CanSubmit);

        connectivity.IsConnected = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsOffline);
        Assert.False(viewModel.CanSubmit);
    }

    [Fact]
    public async Task AServerThatIsDownWhileThePhoneIsOnline_StillReportsALostChange()
    {
        // The case that bit us in dev: wifi is fine, the API is not. Connectivity reads as online,
        // so the button stays enabled - the message is what has to carry the truth here.
        var connectivity = new FakeConnectivityService { IsConnected = true };
        var usersApi = new FakeUsersApi { UpdateMeHandler = _ => throw new HttpRequestException("Connection refused") };
        var viewModel = Settings(connectivity, usersApi);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsOffline);
        Assert.Equal(OfflineWriteMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AFailedRead_KeepsTheReadWording()
    {
        // Nothing was lost on a read - there is just nothing new to show - so it must not claim a
        // change went missing.
        var usersApi = new FakeUsersApi { GetMeHandler = () => throw new HttpRequestException("Connection refused") };
        var viewModel = Settings(new FakeConnectivityService { IsConnected = false }, usersApi);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("You appear to be offline.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AFailedDelete_AlsoSaysTheChangeDidNotLand()
    {
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            GetByIdHandler = id => Task.FromResult(new SubscriptionDto
            {
                Id = id,
                CustomName = "Netflix",
                CostAmount = 19.99m,
                Currency = "INR",
                CycleCadence = BillingCycleType.Monthly,
                PurchaseDate = new DateOnly(2026, 1, 1),
                NextBillingDate = new DateOnly(2026, 8, 1),
                IsActive = true,
            }),
            DeleteHandler = _ => throw new HttpRequestException("Connection refused"),
        };
        var viewModel = Detail(new FakeConnectivityService { IsConnected = false }, subscriptionsApi);
        viewModel.SubscriptionId = Guid.NewGuid();

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(OfflineWriteMessage, viewModel.ErrorMessage);
    }
}
