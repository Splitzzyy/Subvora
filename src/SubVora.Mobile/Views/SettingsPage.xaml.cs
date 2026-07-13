using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class SettingsPage : ContentPage
{
	private readonly SettingsViewModel _viewModel;

	public SettingsPage(SettingsViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
		_viewModel.SignedOut += OnSignedOut;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}

	private async void OnSignedOut(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync($"//{nameof(LoginPage)}");
	}
}
