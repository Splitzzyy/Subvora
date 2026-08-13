using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class DashboardPage : ContentPage
{
	private readonly DashboardViewModel _viewModel;

	public DashboardPage(DashboardViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		// EnsureLoaded, not Load: Shell raises OnAppearing on every tab selection, and a
		// refetch per tab tap is what made the app look like it was permanently refreshing.
		_viewModel.EnsureLoadedCommand.Execute(null);
	}
}
