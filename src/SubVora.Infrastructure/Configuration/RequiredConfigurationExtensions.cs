using Microsoft.Extensions.Configuration;

namespace SubVora.Infrastructure.Configuration;

public static class RequiredConfigurationExtensions
{
    /// <summary>
    /// Reads a configuration value that the app cannot run without, throwing when it is missing
    /// <em>or blank</em>. The distinction matters: appsettings.json ships these keys as empty
    /// strings so no secret lives in source control, and a plain null check lets "" through - which
    /// for Jwt:Secret means booting with an empty signing key instead of failing loudly.
    /// </summary>
    public static string GetRequired(this IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"{key} is not configured. Set it via user secrets locally (dotnet user-secrets set \"{key}\" ...) " +
                $"or the {key.Replace(':', '_').Replace("_", "__")} environment variable in deployed environments.");

    /// <summary>Connection-string flavour of <see cref="GetRequired"/>, since GetConnectionString reads from the ConnectionStrings section.</summary>
    public static string GetRequiredConnectionString(this IConfiguration configuration, string name) =>
        configuration.GetRequired($"ConnectionStrings:{name}");
}
