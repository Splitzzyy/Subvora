using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using CommunityToolkit.Mvvm.Messaging;
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

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
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

    /// <summary>Raised after sign-out completes so the view can navigate back to Login.</summary>
    public event EventHandler? SignedOut;

    public SettingsViewModel(IUsersApi usersApi, IAuthApi authApi, ITokenStore tokenStore, ILocalCacheService localCacheService, IUserPrompt userPrompt, IMessenger messenger, IThemeService themeService)
    {
        _usersApi = usersApi;
        _authApi = authApi;
        _tokenStore = tokenStore;
        _localCacheService = localCacheService;
        _userPrompt = userPrompt;
        _messenger = messenger;
        _themeService = themeService;

        // Seeded from what is already applied, so the picker opens showing the truth rather than
        // resetting the user's choice to System the first time they visit Settings. This re-enters
        // OnThemeChanged and re-applies the value it already has, which is a no-op - the theme was
        // applied at startup, long before this page is built.
        Theme = _themeService.Current;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var profile = await _usersApi.GetMeAsync();

            // Options before the value, so the selection has something to land on. Assigning a
            // currency the list does not contain leaves SelectedCurrency null and the picker blank.
            Currencies = SupportedCurrencies.Including(profile.PreferredCurrency);
            PreferredCurrency = profile.PreferredCurrency;
            DefaultAlertDaysAdvance = profile.DefaultAlertDaysAdvance;
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
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
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
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
