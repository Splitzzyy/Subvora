namespace SubVora.Mobile.Services;

/// <summary>
/// Stand-in <see cref="IPushTokenProvider"/> that never yields a token, used until the Firebase
/// messaging SDK is wired up (#86 - blocked on a human provisioning the Firebase project and its
/// google-services.json / GoogleService-Info.plist). Returning null is the same path a user who
/// refused notification permission takes, so the registration flow around it is already exercised.
/// </summary>
public class UnavailablePushTokenProvider : IPushTokenProvider
{
    public string Platform =>
#if IOS
        "iOS";
#else
        "Android";
#endif

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
