using CommunityToolkit.Maui.Views;

namespace SubVora.Mobile.Views;

/// <summary>
/// Wraps <see cref="ActionSheetView"/> in the toolkit's popup so it can return the chosen action.
/// <para>
/// Thin on purpose: the visuals live in XAML, and this exists only to satisfy the
/// <c>Popup&lt;T&gt;</c> contract - a plain View cannot hand a result back to the awaiting caller.
/// </para>
/// </summary>
public class ActionSheetPopup : Popup<string?>
{
    public ActionSheetPopup(string title, IReadOnlyList<string> actions)
    {
        // Sized to the menu card itself, right-aligned, near the top - where a row-level "..."
        // button sits and where a dropdown from one is expected to appear.
        //
        // Not anchored to the button that opened it: CommunityToolkit's Popup exposes only Margin
        // and the two alignment properties, with no anchor, so tying it to a specific row would
        // mean plumbing that row's screen position through IUserPrompt - and that interface is the
        // seam every view model is tested against. The menu carries the row's name instead.
        VerticalOptions = LayoutOptions.Start;
        HorizontalOptions = LayoutOptions.End;
        Padding = 0;

        // The popup overlay covers the whole window, status bar and navigation bar included, so a
        // small top margin put the menu on top of the purple app bar. This clears both: 24 for the
        // status bar plus 56 for the app bar, then a gap.
        Margin = new Thickness(0, 88, 10, 0);

        var view = new ActionSheetView(title, actions);
        view.ActionChosen += async (_, action) => await CloseAsync(action, CancellationToken.None);

        Content = view;
    }
}
