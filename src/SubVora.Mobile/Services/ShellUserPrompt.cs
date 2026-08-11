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
    public async Task<string?> ActionSheetAsync(string title, string cancel, params string[] actions)
    {
        var result = await Shell.Current.ShowPopupAsync<string?>(
            new ActionSheetPopup(title, actions),
            new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = true });

        // Dismissed without choosing. Null is the caller's cue to do nothing at all - never to fall
        // through to a default action.
        return result.WasDismissedByTappingOutsideOfPopup ? null : result.Result;
    }
}
