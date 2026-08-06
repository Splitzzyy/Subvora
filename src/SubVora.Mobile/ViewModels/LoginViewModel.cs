using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthApi _authApi;
    private readonly ITokenStore _tokenStore;
    private readonly IDevicesApi _devicesApi;
    private readonly IPushTokenProvider _pushTokenProvider;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public event EventHandler? LoginSucceeded;

    public LoginViewModel(IAuthApi authApi, ITokenStore tokenStore, IDevicesApi devicesApi, IPushTokenProvider pushTokenProvider)
    {
        _authApi = authApi;
        _tokenStore = tokenStore;
        _devicesApi = devicesApi;
        _pushTokenProvider = pushTokenProvider;
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

                // Deliberately after the navigation event: push registration is best-effort and
                // must never delay or fail the login.
                await RegisterPushTokenAsync();
                return;
            }

            // Login's own 401 means bad credentials, not an expired session - keep that
            // specific wording rather than the mapper's generic "session expired" message.
            ErrorMessage = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Invalid email or password."
                : ApiErrorMapper.ToDisplayMessage(response);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Registers this device's push token against the freshly authenticated user. Every failure
    /// is swallowed: a user who cannot receive notifications must still be able to use the app,
    /// and the backend upsert on (user_id, token) makes repeat calls harmless.
    /// </summary>
    private async Task RegisterPushTokenAsync()
    {
        try
        {
            var token = await _pushTokenProvider.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            await _devicesApi.RegisterAsync(new RegisterDeviceTokenRequest
            {
                Token = token,
                Platform = _pushTokenProvider.Platform,
            });
        }
        catch (Exception)
        {
            // Intentionally ignored - see the summary above.
        }
    }
}
