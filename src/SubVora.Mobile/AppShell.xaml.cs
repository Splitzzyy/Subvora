using SubVora.Mobile.Services;
using SubVora.Mobile.Views;

namespace SubVora.Mobile;

public partial class AppShell : Shell
{
    private readonly ITokenStore _tokenStore;
    private readonly AuthDelegatingHandler _authDelegatingHandler;
    private readonly IConnectivityService _connectivityService;

    public AppShell(ITokenStore tokenStore, AuthDelegatingHandler authDelegatingHandler, IConnectivityService connectivityService)
    {
        InitializeComponent();

        _tokenStore = tokenStore;
        _authDelegatingHandler = authDelegatingHandler;
        _connectivityService = connectivityService;

        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(SubscriptionDetailPage), typeof(SubscriptionDetailPage));

        _authDelegatingHandler.SessionExpired += OnSessionExpired;

        OfflineBanner.IsVisible = !_connectivityService.IsConnected;
        _connectivityService.ConnectivityChanged += OnConnectivityChanged;

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

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() => OfflineBanner.IsVisible = !isConnected);
    }
}
