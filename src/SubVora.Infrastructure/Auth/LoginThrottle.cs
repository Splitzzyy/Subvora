using System.Collections.Concurrent;

namespace SubVora.Infrastructure.Auth;

/// <summary>
/// Counts failed logins per normalized email, so password guessing spread across many IPs is still
/// bounded per account - the "auth" rate limit only ever saw one IP at a time. After
/// <see cref="MaxFailures"/> failures inside <see cref="Window"/>, the address is refused for
/// <see cref="Window"/>, correct password or not.
///
/// <para>
/// Keyed on the email string, not the user row, so an address with no account locks exactly like
/// one with an account; otherwise "locked" vs "not locked" would tell an attacker which addresses
/// are registered. The cost, accepted deliberately: anyone who knows an address can lock its owner
/// out for a window at a time. Forgot-password does not consult this, and a successful reset clears
/// the lock, so a locked-out user always has a way back in.
/// </para>
/// </summary>
// ponytail: in-process, move to a DB column if the API ever scales past one instance.
public sealed class LoginThrottle
{
    public const int MaxFailures = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>Above this many tracked addresses, a failure also sweeps out the expired ones.</summary>
    private const int SweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public LoginThrottle(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool IsLocked(string normalizedEmail) =>
        _entries.TryGetValue(normalizedEmail, out var entry)
        && entry.Failures >= MaxFailures
        && _timeProvider.GetUtcNow() < entry.ExpiresAt;

    /// <summary>Counts one failure. True only for the failure that engages the lock, so the caller can log it once.</summary>
    public bool RecordFailure(string normalizedEmail)
    {
        var now = _timeProvider.GetUtcNow();
        var entry = _entries.AddOrUpdate(
            normalizedEmail,
            _ => new Entry(1, now + Window),
            (_, current) => current.ExpiresAt <= now
                ? new Entry(1, now + Window)
                // The lock runs a full window from the failure that engaged it, not from the first one.
                : current.Failures + 1 == MaxFailures
                    ? new Entry(MaxFailures, now + Window)
                    : current with { Failures = current.Failures + 1 });

        if (_entries.Count > SweepThreshold)
        {
            foreach (var (key, value) in _entries)
            {
                if (value.ExpiresAt <= now)
                {
                    _entries.TryRemove(KeyValuePair.Create(key, value));
                }
            }
        }

        return entry.Failures == MaxFailures;
    }

    public void Clear(string normalizedEmail) => _entries.TryRemove(normalizedEmail, out _);

    private sealed record Entry(int Failures, DateTimeOffset ExpiresAt);
}
