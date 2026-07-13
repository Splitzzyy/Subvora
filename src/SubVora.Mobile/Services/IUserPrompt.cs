namespace SubVora.Mobile.Services;

/// <summary>
/// Injectable confirmation-dialog abstraction so ViewModels can be unit-tested without a real
/// dialog. Shared by every destructive-action confirmation (delete, sign-out, ...).
/// </summary>
public interface IUserPrompt
{
    Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No");
}
