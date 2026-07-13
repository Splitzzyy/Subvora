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
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadPickersCommand.Execute(null);
	}

	private async void OnSaveSucceeded(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("..");
	}
}
