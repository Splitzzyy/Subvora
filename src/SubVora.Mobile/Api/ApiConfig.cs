using System.Reflection;

namespace SubVora.Mobile.Api;

public static class ApiConfig
{
    /// <summary>
    /// Backend base address, baked in by the <c>ApiBaseAddress</c> MSBuild property (see
    /// SubVora.Mobile.csproj). Overridable per build - <c>-p:ApiBaseAddress=https://staging…</c> -
    /// so pointing a build at another environment never means editing tracked source.
    /// </summary>
    public static string BaseAddress { get; } = Normalize(ReadBuildTimeAddress());

    /// <summary>
    /// How long a request waits before giving up. HttpClient's default is 100 seconds, which is
    /// what the app used to use everywhere: an unreachable host that refuses a connection fails
    /// instantly, but one that simply swallows it - a dead adb tunnel, a sleeping free-tier
    /// instance - left the user watching a spinner for over a minute and a half before the offline
    /// message appeared. Most people force-quit long before that.
    /// <para>
    /// 30s because waking a sleeping Render container takes 40-60s only on a genuine cold start,
    /// and a request that slow is better retried by the user than waited on. Once it fires,
    /// ApiErrorMapper already maps the TaskCanceledException to "You appear to be offline."
    /// </para>
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Shorter than <see cref="RequestTimeout"/> on purpose: a refresh happens inside another
    /// request's 401 retry, so its wait is added on top of the original request's. Sharing the
    /// full timeout would let one user-visible action stall for both.
    /// </summary>
    public static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(15);

    private static string ReadBuildTimeAddress() =>
        typeof(ApiConfig).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "ApiBaseAddress")
            ?.Value ?? "https://localhost:5001/";

    private static string Normalize(string address)
    {
        // 10.0.2.2 is the Android emulator's alias for the host machine; on any other platform it
        // is a stranger's address, and localhost is what reaches a locally-run API.
#if !ANDROID
        address = address.Replace("10.0.2.2", "localhost");
#endif

        // Refit's relative paths need the trailing slash, and a build-time override is easy to
        // hand-write without one.
        return address.EndsWith('/') ? address : address + "/";
    }
}
