using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IUsersApi _usersApi;
    private readonly IAuthApi _authApi;
    private readonly ITokenStore _tokenStore;
    private readonly ILocalCacheService _localCacheService;
    private readonly IUserPrompt _userPrompt;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string PreferredCurrency { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int? DefaultAlertDaysAdvance { get; set; }

    /// <summary>Raised after sign-out completes so the view can navigate back to Login.</summary>
    public event EventHandler? SignedOut;

    public SettingsViewModel(IUsersApi usersApi, IAuthApi authApi, ITokenStore tokenStore, ILocalCacheService localCacheService, IUserPrompt userPrompt, IMessenger messenger)
    {
        _usersApi = usersApi;
        _authApi = authApi;
        _tokenStore = tokenStore;
        _localCacheService = localCacheService;
        _userPrompt = userPrompt;
        _messenger = messenger;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var profile = await _usersApi.GetMeAsync();
            PreferredCurrency = profile.PreferredCurrency;
            DefaultAlertDaysAdvance = profile.DefaultAlertDaysAdvance;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var profile = await _usersApi.UpdateMeAsync(new UpdateUserProfileRequest
            {
                PreferredCurrency = PreferredCurrency,
                DefaultAlertDaysAdvance = DefaultAlertDaysAdvance,
            });
            PreferredCurrency = profile.PreferredCurrency;
            DefaultAlertDaysAdvance = profile.DefaultAlertDaysAdvance;

            // The home currency is what every burn-rate figure is converted into, so changing it
            // moves the headline numbers without a single subscription being touched.
            _messenger.Send(new SubscriptionsChangedMessage());
        }
        catch (ApiException ex)
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var confirmed = await _userPrompt.ConfirmAsync("Sign out", "Are you sure you want to sign out?", "Sign Out", "Cancel");
        if (!confirmed)
        {
            return;
        }

        var refreshToken = await _tokenStore.GetRefreshTokenAsync();
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                await _authApi.LogoutAsync(new RefreshRequest { RefreshToken = refreshToken });
            }
            catch (ApiException)
            {
                // Best-effort server-side revoke - the local session is cleared either way below.
            }
        }

        await _tokenStore.ClearAsync();
        await _localCacheService.ClearAllAsync();

        _messenger.Send(new SessionEndedMessage());
        SignedOut?.Invoke(this, EventArgs.Empty);
    }
}
