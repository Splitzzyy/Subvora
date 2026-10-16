using System.Collections.Concurrent;

namespace SubVora.Infrastructure.Auth;

/// <summary>
/// Counts login attempts per normalized email, so password guessing spread across many IPs is still
/// bounded per account - the "auth" rate limit only ever saw one IP at a time. An address gets
/// <see cref="MaxAttempts"/> attempts; after that it is refused, correct password or not, until
/// <see cref="Window"/> passes with no counted attempt.
///
/// <para>
/// The attempt is reserved before the password is checked, not recorded after. Check-then-record let
/// every request that arrived while the first BCrypt verifies were still running pass the check, so a
/// parallel burst from many IPs got far more than the cap - the same reason ResetPasswordAsync counts
/// a reset-code guess in the database before comparing it.
/// </para>
/// <para>
/// The window slides: it ends <see cref="Window"/> after the latest counted attempt, not the first, so
/// nine attempts either side of a window boundary cannot add up to eighteen.
/// </para>
/// <para>
/// Keyed on the email string, not the user row, so an address with no account locks exactly like
/// one with an account; otherwise "locked" vs "not locked" would tell an attacker which addresses
/// are registered. The cost, accepted deliberately: anyone who knows an address can lock its owner
/// out of password login. Forgot-password does not consult this, and a successful reset signs the
/// user in directly (it returns a token pair) and clears the count, so recovery never depends on
/// the lock.
/// </para>
/// </summary>
// ponytail: in-process, move to a DB column if the API ever scales past one instance.
public sealed class LoginThrottle : IDisposable
{
    public const int MaxAttempts = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _sweepTimer;

    public LoginThrottle(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        // Off the request path: expired entries are dropped on a timer rather than by a scan that
        // every failed login would otherwise pay for.
        _sweepTimer = timeProvider.CreateTimer(_ => Sweep(), null, Window, Window);
    }

    /// <summary>
    /// Reserves one attempt for the address and returns its number inside the window. A number above
    /// <see cref="MaxAttempts"/> means refused: the caller must not check the password.
    /// </summary>
    public int BeginAttempt(string normalizedEmail)
    {
        var now = _timeProvider.GetUtcNow();
        return _entries.AddOrUpdate(
            normalizedEmail,
            _ => new Entry(1, now + Window),
            (_, current) => current.ExpiresAt <= now
                ? new Entry(1, now + Window)
                // Refused attempts are numbered but neither counted nor allowed to extend the lock;
                // otherwise a guesser could hold an address locked by the rate of their own refusals.
                : current.Attempts >= MaxAttempts
                    ? current with { Attempts = current.Attempts + 1 }
                    : new Entry(current.Attempts + 1, now + Window)).Attempts;
    }

    /// <summary>Called on a successful sign-in or password reset.</summary>
    public void Clear(string normalizedEmail) => _entries.TryRemove(normalizedEmail, out _);

    public void Dispose() => _sweepTimer.Dispose();

    private void Sweep()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (key, value) in _entries)
        {
            if (value.ExpiresAt <= now)
            {
                _entries.TryRemove(KeyValuePair.Create(key, value));
            }
        }
    }

    private sealed record Entry(int Attempts, DateTimeOffset ExpiresAt);
}
