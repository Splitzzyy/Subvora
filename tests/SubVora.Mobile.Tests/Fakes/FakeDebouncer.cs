using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>Records the latest scheduled action without waiting on real time. Tests call
/// Flush() to simulate the debounce window elapsing.</summary>
public class FakeDebouncer : IDebouncer
{
    private Action? _pending;

    public int DebounceCallCount { get; private set; }

    public void Debounce(Action action)
    {
        DebounceCallCount++;
        _pending = action;
    }

    public void Flush()
    {
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }
}
