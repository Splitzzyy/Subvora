using Microsoft.Extensions.Logging;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// Keeps every entry - the rendered message, each structured property and the exception text - so
/// a test can assert on everything a log sink would receive.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"))
            : string.Empty;

        lock (_entries)
        {
            _entries.Add($"{formatter(state, exception)} | {properties} | {exception}");
        }
    }
}
