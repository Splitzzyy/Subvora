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
                ErrorMessage = ApiErrorMapper.ToDisplayMessage(registerResponse);
                return;
            }

            // Register answers 202 whether or not the address was already taken - deliberately, so
            // it cannot be used to test which emails have accounts. The login below is what tells
            // the two apart for the person who actually holds the password: right password, they
            // are signed in either way; wrong one, they are pointed at the login screen where
            // "forgot password" lives.
            var loginResponse = await _authApi.LoginAsync(new LoginRequest { Email = Email, Password = Password });
            if (!loginResponse.IsSuccessStatusCode || loginResponse.Content is null)
            {
                ErrorMessage = "Please log in to continue.";
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
