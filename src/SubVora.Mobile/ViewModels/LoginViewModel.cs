using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Notifications;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthApi _authApi;
    private readonly ITokenStore _tokenStore;
    private readonly IRenewalNotificationScheduler _notificationScheduler;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public event EventHandler? LoginSucceeded;

    public LoginViewModel(IAuthApi authApi, ITokenStore tokenStore, IRenewalNotificationScheduler notificationScheduler)
    {
        _authApi = authApi;
        _tokenStore = tokenStore;
        _notificationScheduler = notificationScheduler;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var response = await _authApi.LoginAsync(new LoginRequest { Email = Email, Password = Password });

            if (response.IsSuccessStatusCode && response.Content is not null)
            {
                await _tokenStore.SaveTokensAsync(response.Content);
                LoginSucceeded?.Invoke(this, EventArgs.Empty);

                // Deliberately after the navigation event, and asked here rather than on cold
                // start: the user has just signed in, so "we can remind you before a charge
                // lands" is obvious in a way it is not on a launch screen (user story 4).
                await _notificationScheduler.RequestPermissionAsync();
                return;
            }

            // Login's own 401 means bad credentials, not an expired session - keep that
            // specific wording rather than the mapper's generic "session expired" message.
            ErrorMessage = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Invalid email or password."
                : ApiErrorMapper.ToDisplayMessage(response);
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            // IApiResponse hands back HTTP error statuses as values, which is why this was easy to
            // miss - but an unreachable API throws before any response exists, and an exception
            // escaping a RelayCommand crashes the app rather than showing anything.
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
