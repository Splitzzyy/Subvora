using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IAuthApi _authApi;
    private readonly ITokenStore _tokenStore;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public event EventHandler? RegisterSucceeded;

    public RegisterViewModel(IAuthApi authApi, ITokenStore tokenStore)
    {
        _authApi = authApi;
        _tokenStore = tokenStore;
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        ErrorMessage = null;

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsBusy = true;
        try
        {
            var registerResponse = await _authApi.RegisterAsync(new RegisterRequest { Email = Email, Password = Password });
            if (!registerResponse.IsSuccessStatusCode)
            {
                // 409 (duplicate email) isn't in the mapper's table - keep that specific wording.
                ErrorMessage = registerResponse.StatusCode == HttpStatusCode.Conflict
                    ? "An account with this email already exists."
                    : ApiErrorMapper.ToDisplayMessage(registerResponse);
                return;
            }

            // The register endpoint only returns the created user, not tokens - log the
            // freshly registered account straight in so the caller lands on the Shell.
            var loginResponse = await _authApi.LoginAsync(new LoginRequest { Email = Email, Password = Password });
            if (!loginResponse.IsSuccessStatusCode || loginResponse.Content is null)
            {
                ErrorMessage = "Account created - please log in.";
                return;
            }

            await _tokenStore.SaveTokensAsync(loginResponse.Content);
            RegisterSucceeded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
