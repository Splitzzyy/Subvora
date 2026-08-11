namespace SubVora.Mobile.Services;

public class ShellUserPrompt : IUserPrompt
{
    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No") =>
        Shell.Current.DisplayAlertAsync(title, message, accept, cancel);

    public Task AlertAsync(string title, string message, string dismiss = "OK") =>
        Shell.Current.DisplayAlertAsync(title, message, dismiss);

    public Task<string?> PromptAsync(string title, string message, string initialValue = "") =>
        Shell.Current.DisplayPromptAsync(title, message, initialValue: initialValue);

    public Task<string?> ActionSheetAsync(string title, string cancel, params string[] actions) =>
        Shell.Current.DisplayActionSheetAsync(title, cancel, destruction: null, actions);
}
