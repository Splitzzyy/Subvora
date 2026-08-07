using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Services;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Appearance applies as soon as it is picked and is stored on the device, so it is deliberately not
/// part of what the Save button writes to the user's profile.
/// </summary>
public class ThemeChoiceTests
{
    private static SettingsViewModel CreateViewModel(FakeThemeService themeService) =>
        new(
            new FakeUsersApi(),
            new FakeAuthApi(),
            new FakeTokenStore(),
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            themeService);

    [Fact]
    public void ThePickerOpensOnWhateverIsAlreadyApplied()
    {
        var themeService = new FakeThemeService();
        themeService.Apply(ThemeChoice.Dark);

        var viewModel = CreateViewModel(themeService);

        // Not System - opening Settings must not silently reset a choice the user already made.
        Assert.Equal(ThemeChoice.Dark, viewModel.Theme);
    }

    [Fact]
    public void ChoosingAThemeAppliesItImmediately()
    {
        var themeService = new FakeThemeService();
        var viewModel = CreateViewModel(themeService);
        themeService.Applied.Clear();

        viewModel.Theme = ThemeChoice.Light;

        Assert.Equal([ThemeChoice.Light], themeService.Applied);
    }

    [Fact]
    public void SavingProfileSettingsDoesNotTouchTheTheme()
    {
        var themeService = new FakeThemeService();
        var viewModel = CreateViewModel(themeService);
        viewModel.Theme = ThemeChoice.Dark;
        themeService.Applied.Clear();

        viewModel.SaveCommand.Execute(null);

        // Appearance lives on the device; Save writes the server-side profile.
        Assert.Empty(themeService.Applied);
    }

    [Fact]
    public void AllThreeChoicesAreOffered()
    {
        var viewModel = CreateViewModel(new FakeThemeService());

        Assert.Equal([ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark], viewModel.Themes);
    }
}
