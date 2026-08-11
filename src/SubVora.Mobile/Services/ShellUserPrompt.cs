using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using SubVora.Mobile.Views;

namespace SubVora.Mobile.Services;

public class ShellUserPrompt : IUserPrompt
{
    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No") =>
        Shell.Current.DisplayAlertAsync(title, message, accept, cancel);

    public Task AlertAsync(string title, string message, string dismiss = "OK") =>
        Shell.Current.DisplayAlertAsync(title, message, dismiss);

    public Task<string?> PromptAsync(string title, string message, string initialValue = "") =>
        Shell.Current.DisplayPromptAsync(title, message, initialValue: initialValue);

    /// <summary>
    /// A styled bottom sheet rather than <c>DisplayActionSheetAsync</c>. The platform sheet is an
    /// unstyled list of strings in a system alert - it takes none of the app's shape, colour or type
    /// scale, and read as a different application next to the screen that opened it.
    /// <para>
    /// The <paramref name="cancel"/> label is unused: the sheet is dismissed by tapping outside it,
    /// which is the Material pattern and is what a null return means. It stays in the signature
    /// because <see cref="IUserPrompt"/> is the seam the view models are tested against, and
    /// changing it would churn every caller for no behavioural gain.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public Rect? NextActionSheetAnchor { get; set; }

    public async Task<string?> ActionSheetAsync(string title, string cancel, params string[] actions)
    {
        // Consumed once. Left set, a later menu opened from somewhere with no anchor would position
        // itself against whatever was tapped last.
        var anchor = NextActionSheetAnchor;
        NextActionSheetAnchor = null;

        var result = await Shell.Current.ShowPopupAsync<string?>(
            new ActionSheetPopup(title, actions, anchor),
            new PopupOptions
            {
                CanBeDismissedByTappingOutsideOfPopup = true,

                // The toolkit draws its own rounded card and drop shadow around the content. Left
                // on, they rounded all four corners and floated the sheet clear of the screen edge
                // - the sheet supplies its own top-only corners, so both are cleared here.
                Shape = null,
                Shadow = null,

                // Light scrim only. A menu is not modal in the way a sheet is - it should dim the
                // page enough to read as "on top of" without hiding the row it belongs to, which is
                // the only thing tying the two together.
                PageOverlayColor = Colors.Black.WithAlpha(0.2f),
            });

        // Dismissed without choosing. Null is the caller's cue to do nothing at all - never to fall
        // through to a default action.
        return result.WasDismissedByTappingOutsideOfPopup ? null : result.Result;
    }
}
