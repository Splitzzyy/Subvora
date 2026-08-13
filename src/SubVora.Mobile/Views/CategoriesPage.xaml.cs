using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Views;

public partial class CategoriesPage : ContentPage
{
	private readonly CategoriesViewModel _viewModel;
	private readonly IUserPrompt _userPrompt;

	public CategoriesPage(CategoriesViewModel viewModel, IUserPrompt userPrompt)
	{
		InitializeComponent();
		_viewModel = viewModel;
		_userPrompt = userPrompt;
		BindingContext = _viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		// EnsureLoaded, not Load: Shell raises OnAppearing on every tab selection, and a
		// refetch per tab tap is what made the app look like it was permanently refreshing.
		_viewModel.EnsureLoadedCommand.Execute(null);
	}

	/// <summary>
	/// Opens the row menu, anchored to the button that was tapped.
	/// <para>
	/// The command could be bound straight from XAML, and was. It is routed through here only to
	/// capture where the button is on screen: that is something the page can see and the view model
	/// deliberately cannot, and the alternative - handing a VisualElement to the view model - would
	/// put layout inside the layer that is unit-tested without one.
	/// </para>
	/// </summary>
	private void OnManageTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not VisualElement button || e.Parameter is not CategoryDto category)
		{
			return;
		}

		// Null is fine - ShellUserPrompt falls back to a fixed corner rather than guessing.
		_userPrompt.NextActionSheetAnchor = AnchorBounds.OnScreen(button);

		_viewModel.ManageCommand.Execute(category);
	}
}
