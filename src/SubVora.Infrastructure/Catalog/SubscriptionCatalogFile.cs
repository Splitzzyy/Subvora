using System.Reflection;
using System.Text.Json;

namespace SubVora.Infrastructure.Catalog;

/// <summary>
/// Reads the provider list out of the embedded subscription-catalog.json. Kept separate from the
/// sync service so the file can be validated in a plain unit test, without a database.
/// </summary>
public static class SubscriptionCatalogFile
{
    private const string ResourceName = "SubVora.Infrastructure.Catalog.subscription-catalog.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<SubscriptionCatalogProvider> Read()
    {
        using var stream = typeof(SubscriptionCatalogFile).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"{ResourceName} is not an embedded resource. Check the EmbeddedResource item in SubVora.Infrastructure.csproj.");

        return JsonSerializer.Deserialize<List<SubscriptionCatalogProvider>>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"{ResourceName} did not deserialize to a provider list.");
    }
}
