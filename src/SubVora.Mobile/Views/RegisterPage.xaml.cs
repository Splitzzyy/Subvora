using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _viewModel;

    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.RegisterSucceeded += OnRegisterSucceeded;
    }

    private async void OnRegisterSucceeded(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//DashboardPage");
    }

    private async void OnLoginTapped(object? sender, EventArgs e)
    {
        // This page is only ever pushed from LoginPage, so popping returns there. Navigating to
        // the route by name would stack a second sign-in page on top of the first.
        await Shell.Current.GoToAsync("..");
    }
}
