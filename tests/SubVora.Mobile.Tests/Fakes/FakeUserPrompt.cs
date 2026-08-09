using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeUserPrompt : IUserPrompt
{
    public bool ConfirmResult { get; set; } = true;

    public List<(string Title, string Message)> Calls { get; } = [];

    /// <summary>What a rename prompt returns. Null - the default - is a cancel, not an empty name.</summary>
    public string? PromptResult { get; set; }

    public List<(string Title, string Message)> AlertCalls { get; } = [];
    public List<(string Title, string Message, string InitialValue)> PromptCalls { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        Calls.Add((title, message));
        return Task.FromResult(ConfirmResult);
    }

    public Task AlertAsync(string title, string message, string dismiss = "OK")
    {
        AlertCalls.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<string?> PromptAsync(string title, string message, string initialValue = "")
    {
        PromptCalls.Add((title, message, initialValue));
        return Task.FromResult(PromptResult);
    }
}
