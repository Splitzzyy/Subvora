namespace SubVora.Mobile.Services;

public class ShellUserPrompt : IUserPrompt
{
    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No") =>
        Shell.Current.DisplayAlertAsync(title, message, accept, cancel);
}
