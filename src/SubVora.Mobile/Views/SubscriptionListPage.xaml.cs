using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class SubscriptionListPage : ContentPage
{
	private readonly SubscriptionListViewModel _viewModel;

	public SubscriptionListPage(SubscriptionListViewModel viewModel)
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
