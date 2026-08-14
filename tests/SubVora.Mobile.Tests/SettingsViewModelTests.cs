using CommunityToolkit.Mvvm.Messaging;
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
        FakeAccountApi? accountApi = null,
        FakeTokenStore? tokenStore = null,
        FakeLocalCacheService? cache = null,
        FakeUserPrompt? userPrompt = null,
        IMessenger? messenger = null,
        FakeThemeService? themeService = null) =>
        new(
            usersApi ?? new FakeUsersApi(),
            accountApi ?? new FakeAccountApi(),
            tokenStore ?? new FakeTokenStore(),
            cache ?? new FakeLocalCacheService(),
            userPrompt ?? new FakeUserPrompt(),
            messenger ?? new WeakReferenceMessenger(),
            themeService ?? new FakeThemeService(),
            new FakeConnectivityService());

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
        var accountApi = new FakeAccountApi();
        var userPrompt = new FakeUserPrompt { ConfirmResult = true };
        var viewModel = CreateViewModel(accountApi: accountApi, tokenStore: tokenStore, cache: cache, userPrompt: userPrompt);

        var raised = false;
        viewModel.SignedOut += (_, _) => raised = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.True(tokenStore.Cleared);
        Assert.Single(accountApi.LogoutCalls);
        Assert.Empty(await cache.GetAllAsync<CachedBurnRate>());
        Assert.Empty(await cache.GetAllAsync<CachedSubscription>());
    }

    [Fact]
    public async Task SignOutAsync_RevokesThroughTheClientThatCarriesABearerToken()
    {
        // /auth/logout is [Authorize]. On IAuthApi - registered without AuthDelegatingHandler - the
        // call went out with no token, answered 401, and IApiResponse does not throw, so the refusal
        // was indistinguishable from a successful revoke. The refresh token then stayed live server
        // side for its full 30 days after the user had explicitly signed out.
        var tokenStore = new FakeTokenStore { AccessToken = "access", RefreshToken = "refresh" };
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(
            accountApi: accountApi,
            tokenStore: tokenStore,
            userPrompt: new FakeUserPrompt { ConfirmResult = true });

        await viewModel.SignOutCommand.ExecuteAsync(null);

        var call = Assert.Single(accountApi.LogoutCalls);
        Assert.Equal("refresh", call.RefreshToken);
    }

    [Fact]
    public async Task SignOutAsync_WhenTheRevokeIsRefused_StillEndsTheLocalSessionAndSaysSo()
    {
        // The local session ends unconditionally - that part was never in doubt. What is new is that
        // a refusal is observed rather than silently treated as success.
        var tokenStore = new FakeTokenStore { AccessToken = "access", RefreshToken = "refresh" };
        var accountApi = new FakeAccountApi
        {
            LogoutHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(HttpStatusCode.Unauthorized)),
        };
        var viewModel = CreateViewModel(
            accountApi: accountApi,
            tokenStore: tokenStore,
            userPrompt: new FakeUserPrompt { ConfirmResult = true });

        HttpStatusCode? refusedWith = null;
        viewModel.LogoutRevokeFailed += (_, status) => refusedWith = status;

        var signedOut = false;
        viewModel.SignedOut += (_, _) => signedOut = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, refusedWith);
        Assert.True(signedOut);
        Assert.True(tokenStore.Cleared);
    }

    [Fact]
    public async Task SignOutAsync_WhenTheRevokeSucceeds_RaisesNoFailure()
    {
        var accountApi = new FakeAccountApi();
        var viewModel = CreateViewModel(
            accountApi: accountApi,
            tokenStore: new FakeTokenStore { AccessToken = "access", RefreshToken = "refresh" },
            userPrompt: new FakeUserPrompt { ConfirmResult = true });

        var failed = false;
        viewModel.LogoutRevokeFailed += (_, _) => failed = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.False(failed);
    }

    [Fact]
    public async Task SignOutAsync_WhenDeclined_MakesNoChanges()
    {
        var tokenStore = new FakeTokenStore { AccessToken = "access", RefreshToken = "refresh" };
        var accountApi = new FakeAccountApi();
        var userPrompt = new FakeUserPrompt { ConfirmResult = false };
        var viewModel = CreateViewModel(accountApi: accountApi, tokenStore: tokenStore, userPrompt: userPrompt);

        var raised = false;
        viewModel.SignedOut += (_, _) => raised = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(tokenStore.Cleared);
        Assert.Empty(accountApi.LogoutCalls);
    }
}
