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
}
