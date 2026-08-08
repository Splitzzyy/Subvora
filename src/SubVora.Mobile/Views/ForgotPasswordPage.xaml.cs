using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class ForgotPasswordPage : ContentPage
{
    private readonly ForgotPasswordViewModel _viewModel;

    public ForgotPasswordPage(ForgotPasswordViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.PasswordReset += OnPasswordReset;
    }

    private async void OnPasswordReset(object? sender, EventArgs e)
    {
        // Back to sign-in rather than straight into the app: reset does not issue tokens, and
        // typing the new password once more is the confirmation that it is the one they meant.
        await Shell.Current.GoToAsync("//LoginPage");
        await DisplayAlert("Password changed", "Sign in with your new password.", "OK");
    }

    private async void OnBackToLoginTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
