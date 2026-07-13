using System.Text.Json;
using Refit;

namespace SubVora.Mobile.Services;

/// <summary>
/// Pulls the first field-level validation message out of a failed Refit response's
/// ValidationProblemDetails body, when the API returned one (400 responses).
/// </summary>
public static class ApiValidationErrorParser
{
    public static string? ExtractFirstMessage(IApiResponse response) =>
        ExtractFirstMessage(response.Error as ApiException);

    public static string? ExtractFirstMessage(ApiException? exception)
    {
        var content = exception?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            foreach (var field in errors.EnumerateObject())
            {
                foreach (var message in field.Value.EnumerateArray())
                {
                    var text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
