using SubVora.Mobile.Services;

namespace SubVora.Mobile;

public partial class App : Application
{
	private readonly AppShell _appShell;

	public App(AppShell appShell, IThemeService themeService)
	{
		InitializeComponent();
		_appShell = appShell;

		// Before the first window exists, so the app opens in the chosen appearance rather than
		// flashing the system one and correcting itself.
		themeService.ApplyStored();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_appShell);
	}
}
