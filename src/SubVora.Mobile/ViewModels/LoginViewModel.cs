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

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public event EventHandler? LoginSucceeded;

    public LoginViewModel(IAuthApi authApi, ITokenStore tokenStore)
    {
        _authApi = authApi;
        _tokenStore = tokenStore;
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
                return;
            }

            ErrorMessage = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Invalid email or password.",
                HttpStatusCode.BadRequest => ApiValidationErrorParser.ExtractFirstMessage(response) ?? "Please check your email and password.",
                _ => "Something went wrong. Please try again.",
            };
        }
        finally
        {
            IsBusy = false;
        }
    }
}
