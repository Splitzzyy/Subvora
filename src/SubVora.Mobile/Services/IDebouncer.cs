namespace SubVora.Mobile.Services;

/// <summary>
/// Schedules an action after a quiet period, cancelling any not-yet-fired action from a
/// previous call. Injected so tests can substitute a synchronous fake instead of waiting on
/// real time.
/// </summary>
public interface IDebouncer
{
    void Debounce(Action action);

    /// <summary>
    /// Drops a scheduled action without running it. Needed wherever the reason to run has gone away
    /// during the quiet period - simply not scheduling a new action leaves the previous one armed.
    /// </summary>
    void Cancel();
}
