using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Formatting;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The subscription's own billing currency, now picked rather than typed - matching Settings, with
/// the difference that this is what the subscription is billed in, not a preference.
/// </summary>
public class SubscriptionCurrencyPickerTests
{
    private static SubscriptionDetailViewModel CreateViewModel(FakeSubscriptionsApi? subscriptionsApi = null) =>
        new(
            subscriptionsApi ?? new FakeSubscriptionsApi(),
            new FakeCategoriesApi(),
            new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            new WeakReferenceMessenger(),
            new FakeUserPrompt(),
            new FakeConnectivityService());

    private static FakeSubscriptionsApi ApiReturning(string currency) => new()
    {
        GetByIdHandler = id => Task.FromResult(new SubscriptionDto
        {
            Id = id,
            CustomName = "Netflix",
            CostAmount = 19.99m,
            Currency = currency,
            CycleCadence = BillingCycleType.Monthly,
            PurchaseDate = new DateOnly(2026, 1, 1),
            NextBillingDate = new DateOnly(2026, 8, 1),
            IsActive = true,
        }),
    };

    [Fact]
    public void ANewSubscription_StartsOnTheDefaultCurrency()
    {
        // Deliberately the form's own default, not the user's home currency: this field says what
        // the subscription is billed in, and most are billed locally.
        var viewModel = CreateViewModel();

        Assert.Equal(SupportedCurrencies.DefaultCode, viewModel.Currency);
        Assert.Equal(SupportedCurrencies.DefaultCode, viewModel.SelectedCurrency?.Code);
    }

    [Fact]
    public async Task LoadingASubscription_SelectsItsStoredCurrency()
    {
        var viewModel = CreateViewModel(ApiReturning("GBP"));
        viewModel.SubscriptionId = Guid.NewGuid();

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Equal("GBP", viewModel.Currency);
        Assert.Equal("GBP", viewModel.SelectedCurrency?.Code);
    }

    [Fact]
    public async Task LoadingACurrencyTheRuntimeDoesNotKnow_StillOffersAndSelectsIt()
    {
        // Otherwise the picker lands on nothing and the next save quietly changes what the
        // subscription is billed in.
        var viewModel = CreateViewModel(ApiReturning("ZZZ"));
        viewModel.SubscriptionId = Guid.NewGuid();

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Equal("ZZZ", viewModel.SelectedCurrency?.Code);
        Assert.Contains(viewModel.Currencies, option => option.Code == "ZZZ");
    }

    [Fact]
    public async Task ChoosingACurrency_IsWhatGetsSaved()
    {
        var api = new FakeSubscriptionsApi();
        var viewModel = CreateViewModel(api);
        viewModel.CustomName = "Spotify";
        viewModel.CostAmount = 9.99m;

        viewModel.SelectedCurrency = SupportedCurrencies.Find("EUR");
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("EUR", api.CreateCalls.Single().Currency);
    }

    [Fact]
    public void SettingTheCurrencyDirectly_MovesTheSelectionWithoutLooping()
    {
        var viewModel = CreateViewModel();

        viewModel.Currency = "JPY";

        Assert.Equal("JPY", viewModel.SelectedCurrency?.Code);
        Assert.Equal("JPY", viewModel.Currency);
    }
}
