using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Services;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// What every screen does when the API cannot be reached at all - a stopped container, a dead adb
/// tunnel, no network.
/// <para>
/// Refit only raises ApiException when the server actually answered with an error status. With
/// nothing listening there is no response to wrap, so HttpClient's own HttpRequestException (or a
/// TaskCanceledException on connect timeout) comes through instead. View models that caught only
/// ApiException let it escape the RelayCommand, which takes the app down rather than showing the
/// "You appear to be offline." message the mapper has always had.
/// </para>
/// </summary>
public class OfflineFailureTests
{
    private const string OfflineMessage = "You appear to be offline.";

    /// <summary>What HttpClient throws when nothing is listening on the other end.</summary>
    private static HttpRequestException Unreachable() => new("Connection refused");

    /// <summary>What it throws instead when the host swallows the connection and the connect times out.</summary>
    private static TaskCanceledException TimedOut() => new("The request was canceled due to the configured HttpClient.Timeout");

    [Fact]
    public async Task Login_WhenApiIsUnreachable_ShowsOfflineMessageInsteadOfCrashing()
    {
        var authApi = new FakeAuthApi { LoginHandler = _ => throw Unreachable() };
        var viewModel = new LoginViewModel(authApi, new FakeTokenStore(), new FakeRenewalNotificationScheduler())
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",  // pragma: allowlist secret
        };

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Login_WhenTheConnectTimesOut_ShowsOfflineMessageInsteadOfCrashing()
    {
        var authApi = new FakeAuthApi { LoginHandler = _ => throw TimedOut() };
        var viewModel = new LoginViewModel(authApi, new FakeTokenStore(), new FakeRenewalNotificationScheduler())
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",  // pragma: allowlist secret
        };

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Register_WhenApiIsUnreachable_ShowsOfflineMessageInsteadOfCrashing()
    {
        var authApi = new FakeAuthApi { RegisterHandler = _ => throw Unreachable() };
        var viewModel = new RegisterViewModel(authApi, new FakeTokenStore())
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",  // pragma: allowlist secret
            ConfirmPassword = "correct-horse-battery-staple",  // pragma: allowlist secret
        };

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Categories_WhenApiIsUnreachable_ShowsOfflineMessageInsteadOfCrashing()
    {
        var categoriesApi = new FakeCategoriesApi { GetAllHandler = () => throw Unreachable() };
        var viewModel = new CategoriesViewModel(categoriesApi);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task AddCategory_WhenApiIsUnreachable_ShowsOfflineMessageNotTheDuplicateNameMessage()
    {
        // The 409 special-case in this catch block used to read ex.StatusCode directly, which only
        // exists on ApiException - the transport failure has no status at all.
        var categoriesApi = new FakeCategoriesApi { CreateHandler = _ => throw Unreachable() };
        var viewModel = new CategoriesViewModel(categoriesApi) { NewCategoryName = "Streaming" };

        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task PaymentSources_WhenApiIsUnreachable_ShowsOfflineMessageInsteadOfCrashing()
    {
        var paymentSourcesApi = new FakePaymentSourcesApi { GetAllHandler = () => throw Unreachable() };
        var viewModel = new PaymentSourcesViewModel(paymentSourcesApi, new FakeUserPrompt());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task Settings_WhenApiIsUnreachable_ShowsOfflineMessageInsteadOfCrashing()
    {
        var usersApi = new FakeUsersApi { GetMeHandler = () => throw Unreachable() };
        var viewModel = BuildSettingsViewModel(usersApi, new FakeAuthApi(), new FakeTokenStore());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(OfflineMessage, viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task SignOut_WhenApiIsUnreachable_StillEndsTheLocalSession()
    {
        // Signing out with no connection is the most likely case of all: the server-side revoke is
        // best-effort, but the local session must go either way.
        var authApi = new FakeAuthApi { LogoutHandler = _ => throw Unreachable() };
        var tokenStore = new FakeTokenStore();
        await tokenStore.SaveTokensAsync(FakeAuthApi.SampleTokens());

        var viewModel = BuildSettingsViewModel(new FakeUsersApi(), authApi, tokenStore);

        var signedOut = false;
        viewModel.SignedOut += (_, _) => signedOut = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.True(signedOut);
        Assert.True(tokenStore.Cleared);
        Assert.Null(tokenStore.AccessToken);
    }

    private static SettingsViewModel BuildSettingsViewModel(
        FakeUsersApi usersApi,
        FakeAuthApi authApi,
        FakeTokenStore tokenStore) =>
        new(
            usersApi,
            authApi,
            tokenStore,
            new FakeLocalCacheService(),
            new FakeUserPrompt { ConfirmResult = true },
            new WeakReferenceMessenger(),
            new FakeThemeService());
}
