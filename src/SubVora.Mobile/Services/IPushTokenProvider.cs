namespace SubVora.Mobile.Services;

/// <summary>
/// Wraps the platform's push-messaging SDK the way <see cref="IConnectivityService"/> and
/// <see cref="ITokenStore"/> wrap their platform APIs, so the registration flow can be tested
/// without one.
/// </summary>
public interface IPushTokenProvider
{
    /// <summary>"Android" or "iOS" - the value the backend's device-token validator accepts.</summary>
    string Platform { get; }

    /// <summary>
    /// The device's current push registration token, or null when notification permission was
    /// refused, the platform has no push support, or no token could be obtained.
    /// </summary>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
