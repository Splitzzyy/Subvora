namespace SubVora.Mobile.Services;

/// <summary>
/// Schedules an action after a quiet period, cancelling any not-yet-fired action from a
/// previous call. Injected so tests can substitute a synchronous fake instead of waiting on
/// real time.
/// </summary>
public interface IDebouncer
{
    void Debounce(Action action);
}
