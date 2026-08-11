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
        // Bottom-anchored and full width: a sheet, not a floating dialog.
        VerticalOptions = LayoutOptions.End;
        HorizontalOptions = LayoutOptions.Fill;
        Padding = 0;
        Margin = 0;

        var view = new ActionSheetView(title, actions);
        view.ActionChosen += async (_, action) => await CloseAsync(action, CancellationToken.None);

        Content = view;
    }
}
