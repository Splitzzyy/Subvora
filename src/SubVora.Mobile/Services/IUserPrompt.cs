namespace SubVora.Mobile.Services;

/// <summary>
/// Injectable dialog abstraction so ViewModels can be unit-tested without a real dialog. Shared by
/// every confirmation (delete, sign-out, ...), and by the two below that report or collect
/// something rather than asking a yes/no question.
/// </summary>
public interface IUserPrompt
{
    Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No");

    /// <summary>Tells the user something that has already happened. Nothing to decide, so no cancel.</summary>
    Task AlertAsync(string title, string message, string dismiss = "OK");

    /// <summary>
    /// Asks for a single line of text, pre-filled with <paramref name="initialValue"/>. Null when
    /// the user cancels - which is not the same as clearing the field, and callers must not treat
    /// it as an empty string.
    /// </summary>
    Task<string?> PromptAsync(string title, string message, string initialValue = "");

    /// <summary>
    /// Offers a short list of actions and returns the one chosen, or null when dismissed. Used by
    /// the row-level manage button on Categories and Payment sources, which exists because swiping
    /// advertises itself to nobody.
    /// <para>
    /// Null is a dismissal, not a selection - callers must not fall through to a default action.
    /// </para>
    /// </summary>
    Task<string?> ActionSheetAsync(string title, string cancel, params string[] actions);

    /// <summary>
    /// Screen bounds of the control the next action sheet belongs to, so the menu can open against
    /// it instead of in a corner. Null anchors nothing and the menu falls back to the top right.
    /// <para>
    /// Set by the view immediately before invoking the command, and cleared as soon as it is used.
    /// It lives here rather than as a parameter because the view models call
    /// <see cref="ActionSheetAsync"/> and must not know about views - the position is a fact only
    /// the page has, and threading a VisualElement through a view model to reach this is worse than
    /// one write-then-consume property on the dialog service.
    /// </para>
    /// </summary>
    Rect? NextActionSheetAnchor { get; set; }
}
