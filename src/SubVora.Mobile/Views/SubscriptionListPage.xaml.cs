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
		_viewModel.AddRequested += OnAddRequested;
		_viewModel.SubscriptionSelected += OnSubscriptionSelected;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}

	private async void OnAddRequested(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync(nameof(SubscriptionDetailPage));
	}

	private async void OnSubscriptionSelected(object? sender, Guid id)
	{
		await Shell.Current.GoToAsync(nameof(SubscriptionDetailPage), new Dictionary<string, object> { ["id"] = id });
	}
}
