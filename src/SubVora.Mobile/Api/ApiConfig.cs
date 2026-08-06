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
