using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

/// <summary>
/// Password recovery, in two states on one screen: ask for a code, then spend it.
/// <para>
/// One screen rather than two because the second step needs the email from the first, and carrying
/// it through navigation only to re-display it is more moving parts for no gain. <see cref="CodeSent"/>
/// is what the view switches on.
/// </para>
/// </summary>
public partial class ForgotPasswordViewModel : ObservableObject
{
    private readonly IAuthApi _authApi;
    private readonly IConnectivityService _connectivity;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    /// <summary>Drives the two states. Set once the request is accepted, never unset - going back would discard a code the user may already have received.</summary>
    [ObservableProperty]
    public partial bool CodeSent { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Deliberately separate from <see cref="ErrorMessage"/>: "we've sent a code, if that address has an account" is not a failure and must not be painted as one.</summary>
    [ObservableProperty]
    public partial string? InfoMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsOffline { get; set; }

    public bool CanSubmit => !IsBusy && !IsOffline;

    /// <summary>Raised once the password is actually changed, so the view can return to sign-in.</summary>
    public event EventHandler? PasswordReset;

    public ForgotPasswordViewModel(IAuthApi authApi, IConnectivityService connectivity)
    {
        _authApi = authApi;
        _connectivity = connectivity;
        IsOffline = !connectivity.IsConnected;
    }

    [RelayCommand]
    private async Task RequestCodeAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        IsBusy = true;
        try
        {
            var response = await _authApi.ForgotPasswordAsync(new ForgotPasswordRequest { Email = Email });

            if (!response.IsSuccessStatusCode)
            {
                // Only a malformed address gets here - the endpoint answers 200 for an address it
                // has never seen, on purpose.
                ErrorMessage = ApiErrorMapper.ToDisplayMessage(response);
                return;
            }

            // Hedged, because the server refuses to say whether the address is registered and this
            // screen must not become the oracle it declines to be. "We've sent you a code" would
            // confirm the account exists to anyone who typed a stranger's address.
            InfoMessage = "If that address has an account, we've sent it a 6-digit code. It expires in 15 minutes.";
            CodeSent = true;
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
    private async Task ResetAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;
        IsBusy = true;
        try
        {
            var response = await _authApi.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = Email,
                Code = Code,
                NewPassword = NewPassword,
            });

            if (response.IsSuccessStatusCode)
            {
                PasswordReset?.Invoke(this, EventArgs.Empty);
                return;
            }

            // The server returns one 400 for wrong, expired, already used and over-the-limit,
            // deliberately - so a guesser learns nothing from the wording. Repeating its message
            // verbatim would leak nothing either, but the caller needs to know the code is spent
            // after five tries, hence the hint about requesting another.
            ErrorMessage = response.StatusCode == HttpStatusCode.BadRequest
                ? "That code isn't valid. It may have expired or already been used - request a new one if you need to."
                : ApiErrorMapper.ToDisplayMessage(response);
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
}
