using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Formatting;
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
    private readonly IThemeService _themeService;
    private readonly IConnectivityService _connectivity;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// The stored value and what Save sends. Still the source of truth - the picker below is a way
    /// of setting it, not a replacement for it.
    /// </summary>
    [ObservableProperty]
    public partial string PreferredCurrency { get; set; } = string.Empty;

    /// <summary>
    /// The picker's options. Rebuilt when a profile loads so a code the runtime does not know about
    /// still appears, rather than the picker silently landing on something else and Save writing
    /// that instead.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<CurrencyOption> Currencies { get; set; } = SupportedCurrencies.All;

    /// <summary>
    /// The picker's selection. Kept in step with <see cref="PreferredCurrency"/> in both
    /// directions: choosing an option writes the code, and loading a profile moves the selection.
    /// </summary>
    [ObservableProperty]
    public partial CurrencyOption? SelectedCurrency { get; set; }

    partial void OnSelectedCurrencyChanged(CurrencyOption? value)
    {
        if (value is not null)
        {
            PreferredCurrency = value.Code;
        }
    }

    partial void OnPreferredCurrencyChanged(string value)
    {
        // Guarded, or the two handlers bounce off each other: this assignment re-enters
        // OnSelectedCurrencyChanged, which assigns PreferredCurrency, which re-enters here.
        if (string.Equals(SelectedCurrency?.Code, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Searched against Currencies rather than the full supported set, because Currencies is
        // what the picker is bound to and it may carry an extra entry for a code the runtime does
        // not recognise. Looking in the global list instead left such a profile selecting nothing.
        SelectedCurrency = Currencies.FirstOrDefault(option =>
            string.Equals(option.Code, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [ObservableProperty]
    public partial int? DefaultAlertDaysAdvance { get; set; }

    /// <summary>
    /// Appearance is applied the moment it changes rather than on Save - there is nothing to
    /// confirm about a setting whose result you can see immediately, and it is stored on the device
    /// rather than on the profile the Save button writes.
    /// </summary>
    [ObservableProperty]
    public partial ThemeChoice Theme { get; set; }

    public IReadOnlyList<ThemeChoice> Themes { get; } = Enum.GetValues<ThemeChoice>();

    partial void OnThemeChanged(ThemeChoice value) => _themeService.Apply(value);

    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PasswordMessage { get; set; }

    [ObservableProperty]
    public partial string? PasswordErrorMessage { get; set; }

    /// <summary>Raised after sign-out completes so the view can navigate back to Login.</summary>
    public event EventHandler? SignedOut;

    /// <summary>Raised after the password changes, so the view can confirm it.</summary>
    public event EventHandler? PasswordChanged;

    public SettingsViewModel(IUsersApi usersApi, IAuthApi authApi, ITokenStore tokenStore, ILocalCacheService localCacheService, IUserPrompt userPrompt, IMessenger messenger, IThemeService themeService, IConnectivityService connectivity)
    {
        _usersApi = usersApi;
        _authApi = authApi;
        _tokenStore = tokenStore;
        _localCacheService = localCacheService;
        _userPrompt = userPrompt;
        _messenger = messenger;
        _themeService = themeService;
        _connectivity = connectivity;

        IsOffline = !connectivity.IsConnected;

        // Seeded from what is already applied, so the picker opens showing the truth rather than
        // resetting the user's choice to System the first time they visit Settings. This re-enters
        // OnThemeChanged and re-applies the value it already has, which is a no-op - the theme was
        // applied at startup, long before this page is built.
        Theme = _themeService.Current;

        // Weak registration: the messenger is a singleton and this view model is not. Signing out
        // is published from this same view model, and the handler is what makes the next user's
        // visit fetch their profile instead of showing the previous one's.
        messenger.Register<SessionEndedMessage>(this, (_, _) => Reset());
    }

    /// <summary>
    /// Whether the profile has already been fetched. Shell raises OnAppearing on every tab
    /// selection, so loading unconditionally there meant a request per tab tap - see
    /// <c>SubscriptionListViewModel._isLoaded</c> for the full reasoning. Save applies what the
    /// server returns, so the fields cannot drift from it between visits.
    /// </summary>
    private bool _isLoaded;

    /// <summary>What OnAppearing calls: fetch the profile on the first visit only.</summary>
    [RelayCommand]
    private Task EnsureLoadedAsync() => _isLoaded ? Task.CompletedTask : LoadAsync();

    /// <summary>Drops the signed-out session's profile so the next one is fetched, not inherited.</summary>
    private void Reset()
    {
        _isLoaded = false;
        PreferredCurrency = string.Empty;
        SelectedCurrency = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// Whether the device has no network. Refreshed when the screen loads and after a failed write
    /// rather than by subscribing to the connectivity event: these view models are transient while
    /// IConnectivityService is a singleton, so a subscription would outlive the screen.
    /// <para>
    /// Only covers "this phone has no network". A reachable phone talking to a server that is down
    /// still reads as online - that case is caught by the write failing, with a message saying the
    /// change was not saved.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsOffline { get; set; }

    /// <summary>Gates the write button, so an edit that cannot possibly succeed is not offered.</summary>
    public bool CanSubmit => !IsOffline && !IsBusy;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        IsOffline = !_connectivity.IsConnected;
        try
        {
            var profile = await _usersApi.GetMeAsync();

            // Options before the value, so the selection has something to land on. Assigning a
            // currency the list does not contain leaves SelectedCurrency null and the picker blank.
            Currencies = SupportedCurrencies.Including(profile.PreferredCurrency);
            PreferredCurrency = profile.PreferredCurrency;
            DefaultAlertDaysAdvance = profile.DefaultAlertDaysAdvance;
            _isLoaded = true;
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            // _isLoaded stays false: the fields hold nothing, so the next visit should retry.
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
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        PasswordErrorMessage = null;
        PasswordMessage = null;
        IsBusy = true;
        try
        {
            var response = await _authApi.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = CurrentPassword,
                NewPassword = NewPassword,
            });

            if (!response.IsSuccessStatusCode || response.Content is null)
            {
                // 400 covers both a wrong current password and a new one that fails validation;
                // the server's own message distinguishes them, so surface that rather than guess.
                PasswordErrorMessage = ApiErrorMapper.ToDisplayMessage(response);
                return;
            }

            // Succeeding revoked every refresh token on the account, this device's included. The
            // replacement pair has to be stored or the next 401 signs the user out of the phone
            // they just used - which is the change appearing to have broken the app.
            await _tokenStore.SaveTokensAsync(response.Content);

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;  // pragma: allowlist secret
            PasswordMessage = "Password changed. Any other devices have been signed out.";  // pragma: allowlist secret
            PasswordChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            PasswordErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
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
            catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
            {
                // Best-effort server-side revoke - the local session is cleared either way below.
                // Signing out with the API unreachable is the most likely case of all, and must
                // still end the local session rather than crash.
            }
        }

        await _tokenStore.ClearAsync();
        await _localCacheService.ClearAllAsync();

        _messenger.Send(new SessionEndedMessage());
        SignedOut?.Invoke(this, EventArgs.Empty);
    }
}
