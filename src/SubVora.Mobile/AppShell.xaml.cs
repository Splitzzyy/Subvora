using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Services;
using SubVora.Mobile.ViewModels;
using SubVora.Mobile.Views;

namespace SubVora.Mobile;

public partial class AppShell : Shell
{
    private readonly ITokenStore _tokenStore;
    private readonly AuthDelegatingHandler _authDelegatingHandler;
    private readonly IConnectivityService _connectivityService;
    private readonly IMessenger _messenger;

    public AppShell(
        ITokenStore tokenStore,
        AuthDelegatingHandler authDelegatingHandler,
        IConnectivityService connectivityService,
        DashboardViewModel dashboardViewModel,
        IMessenger messenger)
    {
        InitializeComponent();

        _tokenStore = tokenStore;
        _authDelegatingHandler = authDelegatingHandler;
        _connectivityService = connectivityService;
        _messenger = messenger;

        // The same singleton the dashboard page binds to, so the banner and the page can never
        // disagree about what the user is spending.
        BindingContext = dashboardViewModel;

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
        // Clear before navigating: the login page is inside this Shell, so the banner would
        // otherwise still be showing the expired session's figures behind the sign-in form.
        _messenger.Send(new SessionEndedMessage());
        MainThread.BeginInvokeOnMainThread(async () => await GoToAsync($"//{nameof(LoginPage)}"));
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() => OfflineBanner.IsVisible = !isConnected);
    }
}
