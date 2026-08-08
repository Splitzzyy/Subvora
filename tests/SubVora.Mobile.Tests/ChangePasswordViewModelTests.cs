using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class ChangePasswordViewModelTests
{
    private static SettingsViewModel CreateViewModel(FakeAuthApi authApi, FakeTokenStore tokenStore, FakeConnectivityService? connectivity = null) =>
        new(
            new FakeUsersApi(),
            authApi,
            tokenStore,
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            new FakeThemeService(),
            connectivity ?? new FakeConnectivityService());

    [Fact]
    public async Task ChangePassword_OnSuccess_StoresTheReplacementTokens()
    {
        // The change revoked every refresh token on the account, this device's included. Without
        // storing the replacement pair the next 401 signs the user out of the phone they are
        // holding - the change looking like it broke the app.
        var tokenStore = new FakeTokenStore();
        var viewModel = CreateViewModel(new FakeAuthApi(), tokenStore);
        viewModel.CurrentPassword = "correct-horse-battery-staple";      // pragma: allowlist secret
        viewModel.NewPassword = "an-entirely-different-passphrase";      // pragma: allowlist secret

        await viewModel.ChangePasswordCommand.ExecuteAsync(null);

        Assert.Equal(FakeAuthApi.SampleTokens().AccessToken, tokenStore.AccessToken);
        Assert.Equal(FakeAuthApi.SampleTokens().RefreshToken, tokenStore.RefreshToken);
    }

    [Fact]
    public async Task ChangePassword_OnSuccess_ClearsTheFieldsAndConfirms()
    {
        var viewModel = CreateViewModel(new FakeAuthApi(), new FakeTokenStore());
        viewModel.CurrentPassword = "correct-horse-battery-staple";  // pragma: allowlist secret
        viewModel.NewPassword = "an-entirely-different-passphrase";  // pragma: allowlist secret

        var raised = false;
        viewModel.PasswordChanged += (_, _) => raised = true;

        await viewModel.ChangePasswordCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal(string.Empty, viewModel.CurrentPassword);
        Assert.Equal(string.Empty, viewModel.NewPassword);
        Assert.NotNull(viewModel.PasswordMessage);
        Assert.Null(viewModel.PasswordErrorMessage);
    }

    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPassword_KeepsTheSessionAndExplains()
    {
        var authApi = new FakeAuthApi
        {
            ChangePasswordHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(
                HttpStatusCode.BadRequest,
                content: null,
                validationErrorJson: """{"title":"Your current password is incorrect."}""")),
        };
        var tokenStore = new FakeTokenStore();
        await tokenStore.SaveTokensAsync(FakeAuthApi.SampleTokens());
        var existingToken = tokenStore.AccessToken;

        var viewModel = CreateViewModel(authApi, tokenStore);
        viewModel.CurrentPassword = "wrong";                             // pragma: allowlist secret
        viewModel.NewPassword = "an-entirely-different-passphrase";      // pragma: allowlist secret

        await viewModel.ChangePasswordCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.PasswordErrorMessage);
        // Nothing changed server-side, so the session must be left exactly as it was.
        Assert.Equal(existingToken, tokenStore.AccessToken);
    }

    [Fact]
    public async Task ChangePassword_KeepsItsMessagesSeparateFromThePreferencesCard()
    {
        // Both live on the same screen. A failed password change must not paint the currency card
        // red, and vice versa.
        var authApi = new FakeAuthApi
        {
            ChangePasswordHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(HttpStatusCode.BadRequest, content: null)),
        };
        var viewModel = CreateViewModel(authApi, new FakeTokenStore());
        viewModel.CurrentPassword = "wrong";  // pragma: allowlist secret

        await viewModel.ChangePasswordCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.PasswordErrorMessage);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ChangePassword_WhenOffline_SaysTheChangeDidNotLand()
    {
        var authApi = new FakeAuthApi { ChangePasswordHandler = _ => throw new HttpRequestException("Connection refused") };
        var viewModel = CreateViewModel(authApi, new FakeTokenStore(), new FakeConnectivityService { IsConnected = false });
        viewModel.CurrentPassword = "correct-horse-battery-staple";  // pragma: allowlist secret

        await viewModel.ChangePasswordCommand.ExecuteAsync(null);

        Assert.Equal("You're offline — this change wasn't saved. Try again once you're connected.", viewModel.PasswordErrorMessage);
        Assert.False(viewModel.IsBusy);
    }
}
