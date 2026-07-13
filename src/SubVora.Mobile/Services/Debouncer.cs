namespace SubVora.Mobile.Services;

public class Debouncer : IDebouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cancellationTokenSource;

    public Debouncer(TimeSpan? delay = null)
    {
        _delay = delay ?? TimeSpan.FromMilliseconds(500);
    }

    public void Debounce(Action action)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();

        var cts = new CancellationTokenSource();
        _cancellationTokenSource = cts;

        _ = RunAfterDelayAsync(action, cts.Token);
    }

    private async Task RunAfterDelayAsync(Action action, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_delay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            action();
        }
    }
}
