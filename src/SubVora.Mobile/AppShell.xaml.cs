using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Services;
using SubVora.Mobile.Views;

namespace SubVora.Mobile;

public partial class AppShell : Shell
{
    private readonly ITokenStore _tokenStore;
    private readonly SessionRefresher _sessionRefresher;
    private readonly IMessenger _messenger;

    public AppShell(
        ITokenStore tokenStore,
        SessionRefresher sessionRefresher,
        IMessenger messenger)
    {
        InitializeComponent();

        _tokenStore = tokenStore;
        _sessionRefresher = sessionRefresher;
        _messenger = messenger;

        // LoginPage is declared in AppShell.xaml instead - see the comment there.
        Routing.RegisterRoute(nameof(RegisterPage), typeof(RegisterPage));
        Routing.RegisterRoute(nameof(ForgotPasswordPage), typeof(ForgotPasswordPage));
        Routing.RegisterRoute(nameof(SubscriptionDetailPage), typeof(SubscriptionDetailPage));

        _sessionRefresher.SessionExpired += OnSessionExpired;

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
        // Clear before navigating: the login page is inside this Shell, so the dashboard would
        // otherwise still be holding the expired session's figures behind the sign-in form.
        _messenger.Send(new SessionEndedMessage());
        MainThread.BeginInvokeOnMainThread(async () => await GoToAsync($"//{nameof(LoginPage)}"));
    }
}
