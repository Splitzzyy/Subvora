using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>
/// Connected by default, so every existing test keeps the behaviour it was written against. Flip
/// <see cref="IsConnected"/> mid-test to model a connection dropping while a screen is open - the
/// view models re-read it rather than subscribing, so nothing has to be raised.
/// </summary>
public class FakeConnectivityService : IConnectivityService
{
    public bool IsConnected { get; set; } = true;
}
