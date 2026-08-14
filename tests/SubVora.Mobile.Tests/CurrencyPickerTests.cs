using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Formatting;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The home-currency picker in Settings. The stored value is still the ISO code; the picker is a
/// way of setting it without knowing that code by heart.
/// </summary>
public class CurrencyPickerTests
{
    private static SettingsViewModel CreateViewModel(FakeUsersApi usersApi) =>
        new(
            usersApi,
            new FakeAccountApi(),
            new FakeTokenStore(),
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            new FakeThemeService(),
            new FakeConnectivityService());

    private static FakeUsersApi UsersApiReturning(string preferredCurrency) => new()
    {
        GetMeHandler = () => Task.FromResult(new UserProfileDto
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PreferredCurrency = preferredCurrency,
            DefaultAlertDaysAdvance = 3,
        }),
    };

    [Fact]
    public void SupportedCurrencies_OfferTheDefaultAndTheOnesPeopleActuallyUse()
    {
        var codes = SupportedCurrencies.All.Select(option => option.Code).ToHashSet();

        Assert.Contains(SupportedCurrencies.DefaultCode, codes);
        Assert.Contains("USD", codes);
        Assert.Contains("EUR", codes);
        Assert.Contains("GBP", codes);
        Assert.Contains("JPY", codes);
    }

    [Fact]
    public void SupportedCurrencies_AreSortedByCodeAndUnique()
    {
        var codes = SupportedCurrencies.All.Select(option => option.Code).ToList();

        // Ordinal, so the order does not shift with the device's language - a picker that reorders
        // itself when the phone language changes would be its own bug report.
        Assert.Equal(codes.OrderBy(code => code, StringComparer.Ordinal), codes);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void CurrencyOption_ReadsAsSymbolCodeAndName() =>
        Assert.Equal("₹  INR — Indian Rupee", new CurrencyOption("INR", "Indian Rupee").Display);

    [Fact]
    public async Task LoadAsync_SelectsTheStoredCurrency()
    {
        var viewModel = CreateViewModel(UsersApiReturning("GBP"));

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("GBP", viewModel.SelectedCurrency?.Code);
        Assert.Equal("GBP", viewModel.PreferredCurrency);
    }

    [Fact]
    public async Task ChoosingACurrency_UpdatesTheValueThatGetsSaved()
    {
        var usersApi = UsersApiReturning("INR");
        var viewModel = CreateViewModel(usersApi);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SelectedCurrency = SupportedCurrencies.Find("EUR");
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("EUR", Assert.Single(usersApi.UpdateMeCalls).PreferredCurrency);
    }

    [Fact]
    public async Task LoadAsync_WithACurrencyTheRuntimeDoesNotKnow_StillOffersAndSelectsIt()
    {
        // Saved by another client, or a code that has left circulation. Without this the picker
        // lands on nothing, and a Save made for some unrelated reason silently rewrites it.
        var viewModel = CreateViewModel(UsersApiReturning("ZZZ"));

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("ZZZ", viewModel.SelectedCurrency?.Code);
        Assert.Contains(viewModel.Currencies, option => option.Code == "ZZZ");
    }

    [Fact]
    public async Task LoadAsync_WhenTheProfileCannotBeFetched_LeavesTheSelectionEmpty()
    {
        // Deliberately not defaulting to INR here. Showing a currency the user never chose, on a
        // screen whose Save button is one tap away, would overwrite whatever is really stored.
        var usersApi = new FakeUsersApi { GetMeHandler = () => throw new HttpRequestException("offline") };
        var viewModel = CreateViewModel(usersApi);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedCurrency);
    }

    [Fact]
    public void SettingThePreferredCurrencyDirectly_MovesTheSelectionWithoutLooping()
    {
        // The two properties update each other; the guard in the view model is what stops that
        // being infinite recursion rather than a stack overflow on the settings screen.
        var viewModel = CreateViewModel(new FakeUsersApi());

        viewModel.PreferredCurrency = "JPY";

        Assert.Equal("JPY", viewModel.SelectedCurrency?.Code);
        Assert.Equal("JPY", viewModel.PreferredCurrency);
    }
}
