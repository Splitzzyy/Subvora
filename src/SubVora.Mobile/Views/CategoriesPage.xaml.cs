using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class CategoriesPage : ContentPage
{
	private readonly CategoriesViewModel _viewModel;

	public CategoriesPage(CategoriesViewModel viewModel)
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
