using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class SubscriptionDetailPage : ContentPage
{
	private readonly SubscriptionDetailViewModel _viewModel;

	public SubscriptionDetailPage(SubscriptionDetailViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
		_viewModel.SaveSucceeded += OnSaveSucceeded;
		_viewModel.SubscriptionNotFound += OnSubscriptionNotFound;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.InitializeCommand.Execute(null);
	}

	private async void OnSaveSucceeded(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("..");
	}

	private async void OnSubscriptionNotFound(object? sender, EventArgs e)
	{
		await Shell.Current.DisplayAlertAsync("Not found", "This subscription no longer exists.", "OK");
		await Shell.Current.GoToAsync("..");
	}
}
