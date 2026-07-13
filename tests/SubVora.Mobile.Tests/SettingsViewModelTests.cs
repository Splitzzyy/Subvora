using System.Net;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(
        FakeUsersApi? usersApi = null,
        FakeAuthApi? authApi = null,
        FakeTokenStore? tokenStore = null,
        FakeLocalCacheService? cache = null,
        FakeUserPrompt? userPrompt = null) =>
        new(
            usersApi ?? new FakeUsersApi(),
            authApi ?? new FakeAuthApi(),
            tokenStore ?? new FakeTokenStore(),
            cache ?? new FakeLocalCacheService(),
            userPrompt ?? new FakeUserPrompt());

    [Fact]
    public async Task LoadAsync_PopulatesPreferredCurrencyAndDefaultAlertDaysAdvance()
    {
        var usersApi = new FakeUsersApi
        {
            GetMeHandler = () => Task.FromResult(new UserProfileDto
            {
                Id = Guid.NewGuid(),
                Email = "user@example.com",
                PreferredCurrency = "EUR",
                DefaultAlertDaysAdvance = 5,
            }),
        };
        var viewModel = CreateViewModel(usersApi);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("EUR", viewModel.PreferredCurrency);
        Assert.Equal(5, viewModel.DefaultAlertDaysAdvance);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_CallsUpdateMeWithEditedValues()
    {
        var usersApi = new FakeUsersApi();
        var viewModel = CreateViewModel(usersApi);
        viewModel.PreferredCurrency = "GBP";
        viewModel.DefaultAlertDaysAdvance = 7;

        await viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(usersApi.UpdateMeCalls);
        Assert.Equal("GBP", request.PreferredCurrency);
        Assert.Equal(7, request.DefaultAlertDaysAdvance);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_WithInvalidCurrency_SurfacesApiErrorInline()
    {
        var usersApi = new FakeUsersApi
        {
            UpdateMeHandler = _ => throw TestApiExceptions.Create(
                HttpStatusCode.BadRequest,
                """{"errors":{"PreferredCurrency":["'Preferred Currency' must be a valid ISO-4217 currency code."]}}"""),
        };
        var viewModel = CreateViewModel(usersApi);
        viewModel.PreferredCurrency = "XX";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("'Preferred Currency' must be a valid ISO-4217 currency code.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SignOutAsync_WhenConfirmed_ClearsTokenStoreAndCacheThenRaisesSignedOut()
    {
        var tokenStore = new FakeTokenStore { AccessToken = "access", RefreshToken = "refresh" };
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(new CachedBurnRate { Weekly = 10 });
        await cache.UpsertAsync(new CachedSubscription { Id = Guid.NewGuid(), CustomName = "Netflix" });
        var authApi = new FakeAuthApi();
        var userPrompt = new FakeUserPrompt { ConfirmResult = true };
        var viewModel = CreateViewModel(authApi: authApi, tokenStore: tokenStore, cache: cache, userPrompt: userPrompt);

        var raised = false;
        viewModel.SignedOut += (_, _) => raised = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.True(tokenStore.Cleared);
        Assert.Single(authApi.LogoutCalls);
        Assert.Empty(await cache.GetAllAsync<CachedBurnRate>());
        Assert.Empty(await cache.GetAllAsync<CachedSubscription>());
    }

    [Fact]
    public async Task SignOutAsync_WhenDeclined_MakesNoChanges()
    {
        var tokenStore = new FakeTokenStore { AccessToken = "access", RefreshToken = "refresh" };
        var authApi = new FakeAuthApi();
        var userPrompt = new FakeUserPrompt { ConfirmResult = false };
        var viewModel = CreateViewModel(authApi: authApi, tokenStore: tokenStore, userPrompt: userPrompt);

        var raised = false;
        viewModel.SignedOut += (_, _) => raised = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(tokenStore.Cleared);
        Assert.Empty(authApi.LogoutCalls);
    }
}
