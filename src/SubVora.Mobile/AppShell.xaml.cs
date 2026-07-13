using SubVora.Mobile.Services;
using SubVora.Mobile.Views;

namespace SubVora.Mobile;

public partial class AppShell : Shell
{
    private readonly ITokenStore _tokenStore;
    private readonly AuthDelegatingHandler _authDelegatingHandler;

    public AppShell(ITokenStore tokenStore, AuthDelegatingHandler authDelegatingHandler)
    {
        InitializeComponent();

        _tokenStore = tokenStore;
        _authDelegatingHandler = authDelegatingHandler;

        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(SubscriptionDetailPage), typeof(SubscriptionDetailPage));

        _authDelegatingHandler.SessionExpired += OnSessionExpired;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;

        var accessToken = await _tokenStore.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(accessToken))
        {
            await GoToAsync($"//{nameof(LoginPage)}");
        }
    }

    private void OnSessionExpired(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () => await GoToAsync($"//{nameof(LoginPage)}"));
    }
}
