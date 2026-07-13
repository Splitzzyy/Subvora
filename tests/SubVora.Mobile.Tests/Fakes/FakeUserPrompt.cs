using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeUserPrompt : IUserPrompt
{
    public bool ConfirmResult { get; set; } = true;

    public List<(string Title, string Message)> Calls { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        Calls.Add((title, message));
        return Task.FromResult(ConfirmResult);
    }
}
