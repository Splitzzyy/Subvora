using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>
/// The real one touches Preferences and Application.Current, neither of which exists in a unit test.
/// </summary>
public class FakeThemeService : IThemeService
{
    public ThemeChoice Current { get; private set; } = ThemeChoice.System;

    public List<ThemeChoice> Applied { get; } = [];

    public bool AppliedStored { get; private set; }

    public void ApplyStored() => AppliedStored = true;

    public void Apply(ThemeChoice choice)
    {
        Current = choice;
        Applied.Add(choice);
    }
}
