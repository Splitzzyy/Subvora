using System.Collections.Concurrent;
using SubVora.Application.Auth;

namespace SubVora.Api.Tests;

/// <summary>Reports breached only the passwords a test adds to <see cref="Breached"/>.</summary>
public sealed class FakeBreachedPasswordChecker : IBreachedPasswordChecker
{
    public ConcurrentDictionary<string, bool> Breached { get; } = new();

    public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default) =>
        Task.FromResult(Breached.ContainsKey(password));
}
