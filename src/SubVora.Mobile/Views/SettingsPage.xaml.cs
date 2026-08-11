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

		ShowAppVersion();
	}

	/// <summary>
	/// Fills in the About lines at the bottom of the page.
	/// <para>
	/// Here rather than on the view model on purpose: <c>AppInfo.Current</c> is a static platform
	/// accessor, the view model is unit-tested in a plain xUnit host where it is not reliably
	/// available, and a display-only string has no logic worth injecting an abstraction for.
	/// </para>
	/// <para>
	/// Wrapped because it is decoration: a platform that cannot answer must not take the whole
	/// Settings page down over a version string.
	/// </para>
	/// </summary>
	private void ShowAppVersion()
	{
		try
		{
			var info = AppInfo.Current;
			VersionLabel.Text = $"SubVora {info.VersionString} (build {info.BuildString})";
			PackageLabel.Text = $"{DeviceInfo.Current.Platform} · {info.PackageName}";
		}
		catch (Exception)
		{
			VersionLabel.Text = "SubVora";
			PackageLabel.Text = string.Empty;
		}
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
