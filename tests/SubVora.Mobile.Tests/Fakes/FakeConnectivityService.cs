using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>Connected by default, so every existing test keeps the behaviour it was written against.</summary>
public class FakeConnectivityService : IConnectivityService
{
    public bool IsConnected { get; set; } = true;

    public event EventHandler<bool>? ConnectivityChanged;

    public void Report(bool isConnected)
    {
        IsConnected = isConnected;
        ConnectivityChanged?.Invoke(this, isConnected);
    }
}
