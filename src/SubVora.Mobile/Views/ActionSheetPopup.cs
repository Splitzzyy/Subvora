using CommunityToolkit.Maui.Views;

namespace SubVora.Mobile.Views;

/// <summary>
/// Wraps <see cref="ActionSheetView"/> in the toolkit's popup so it can return the chosen action,
/// and positions it against the control that opened it.
/// </summary>
public class ActionSheetPopup : Popup<string?>
{
    /// <summary>Must match the card's WidthRequest in ActionSheetView.xaml.</summary>
    private const double MenuWidth = 212;

    /// <summary>Title row plus one row per action, near enough to decide whether the menu fits below the anchor.</summary>
    private const double EstimatedRowHeight = 40;
    private const double EstimatedChromeHeight = 40;

    private const double EdgeGap = 8;
    private const double AnchorGap = 6;

    /// <param name="anchor">
    /// Screen bounds of the control that opened the menu, in device-independent units, or null when
    /// the caller could not work them out - in which case the menu falls back to the top right,
    /// below the app bar.
    /// </param>
    public ActionSheetPopup(string title, IReadOnlyList<string> actions, Rect? anchor = null)
    {
        VerticalOptions = LayoutOptions.Start;
        HorizontalOptions = LayoutOptions.Start;
        Padding = 0;
        Margin = PositionFor(anchor, actions.Count);

        var view = new ActionSheetView(title, actions);
        view.ActionChosen += async (_, action) => await CloseAsync(action, CancellationToken.None);

        Content = view;
    }

    /// <summary>
    /// Where to put the menu. The popup overlay covers the whole window - status bar included - so
    /// these are plain screen coordinates and a margin is the only positioning the toolkit offers:
    /// its Popup has no anchor of its own.
    /// </summary>
    private static Thickness PositionFor(Rect? anchor, int actionCount)
    {
        var display = DeviceDisplay.Current.MainDisplayInfo;
        var screenWidth = display.Width / display.Density;
        var screenHeight = display.Height / display.Density;

        if (anchor is not { } bounds)
        {
            // No anchor: top right, clear of the 24pt status bar and 56pt app bar.
            return new Thickness(Math.Max(EdgeGap, screenWidth - MenuWidth - 10), 88, 0, 0);
        }

        // Right edges aligned, so the menu hangs from the button rather than starting at it - the
        // button sits at the right end of its row, and a left-aligned menu would run off-screen.
        var left = Math.Clamp(bounds.Right - MenuWidth, EdgeGap, Math.Max(EdgeGap, screenWidth - MenuWidth - EdgeGap));

        var menuHeight = EstimatedChromeHeight + (actionCount * EstimatedRowHeight);
        var below = bounds.Bottom + AnchorGap;

        // Flip above the button when there is not room beneath it, which is what happens for a row
        // near the bottom of a long list.
        var top = below + menuHeight + EdgeGap <= screenHeight
            ? below
            : Math.Max(EdgeGap, bounds.Top - menuHeight - AnchorGap);

        return new Thickness(left, top, 0, 0);
    }
}
