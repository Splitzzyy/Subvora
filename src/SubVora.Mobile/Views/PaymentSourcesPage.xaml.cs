using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class PaymentSourcesPage : ContentPage
{
	private readonly PaymentSourcesViewModel _viewModel;

	public PaymentSourcesPage(PaymentSourcesViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}
}
