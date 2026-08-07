namespace SubVora.Mobile.Services;

public enum ThemeChoice
{
    /// <summary>Follow the phone's own light/dark setting.</summary>
    System,
    Light,
    Dark,
}

public interface IThemeService
{
    ThemeChoice Current { get; }

    /// <summary>Applies the stored choice. Call once at startup, before the first page is shown.</summary>
    void ApplyStored();

    void Apply(ThemeChoice choice);
}

/// <summary>
/// Stores the user's light/dark choice on the device and applies it to the app.
/// <para>
/// Device-local rather than part of the user profile on the server: the same account on a phone and
/// a tablet can reasonably want different appearances, and the choice should survive being offline
/// and signed out.
/// </para>
/// </summary>
public class ThemeService : IThemeService
{
    private const string PreferenceKey = "app_theme";

    public ThemeChoice Current { get; private set; } = ThemeChoice.System;

    public void ApplyStored()
    {
        var stored = Preferences.Default.Get(PreferenceKey, nameof(ThemeChoice.System));
        Apply(Enum.TryParse<ThemeChoice>(stored, out var choice) ? choice : ThemeChoice.System);
    }

    public void Apply(ThemeChoice choice)
    {
        Current = choice;
        Preferences.Default.Set(PreferenceKey, choice.ToString());

        if (Application.Current is not { } app)
        {
            return;
        }

        // AppTheme.Unspecified is what makes MAUI defer to the OS again, so "System" is a real
        // choice rather than just whatever was last applied.
        app.UserAppTheme = choice switch
        {
            ThemeChoice.Light => AppTheme.Light,
            ThemeChoice.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }
}
