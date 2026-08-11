using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class PaymentSourcesPage : ContentPage
{
	private readonly PaymentSourcesViewModel _viewModel;
	private readonly IUserPrompt _userPrompt;

	public PaymentSourcesPage(PaymentSourcesViewModel viewModel, IUserPrompt userPrompt)
	{
		InitializeComponent();
		_viewModel = viewModel;
		_userPrompt = userPrompt;
		BindingContext = _viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}

	/// <summary>
	/// Opens the row menu anchored to the button tapped - see CategoriesPage.OnManageTapped for why
	/// this goes through the page rather than binding the command directly.
	/// </summary>
	private void OnManageClicked(object? sender, EventArgs e)
	{
		if (sender is not Button button || button.CommandParameter is not PaymentSourceDto paymentSource)
		{
			return;
		}

		_userPrompt.NextActionSheetAnchor = AnchorBounds.OnScreen(button);

		_viewModel.ManageCommand.Execute(paymentSource);
	}
}
