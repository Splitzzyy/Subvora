using System.Net;
using Refit;

namespace SubVora.Mobile.Services;

/// <summary>
/// Centralized API-error-to-display-message mapping, reused by every ViewModel's catch block
/// instead of ad hoc per-screen string handling.
/// </summary>
public static class ApiErrorMapper
{
    public static string ToDisplayMessage(Exception exception) => exception switch
    {
        ApiException apiException => ToDisplayMessage(apiException),
        HttpRequestException or TaskCanceledException => "You appear to be offline.",
        _ => "Something went wrong. Please try again.",
    };

    public static string ToDisplayMessage(ApiException exception) => ToDisplayMessage(exception.StatusCode, exception);

    public static string ToDisplayMessage(IApiResponse response) => ToDisplayMessage(response.StatusCode ?? 0, response.Error as ApiException);

    private static string ToDisplayMessage(HttpStatusCode statusCode, ApiException? exception) => statusCode switch
    {
        HttpStatusCode.BadRequest => ApiValidationErrorParser.ExtractFirstMessage(exception) ?? "Please check your input and try again.",
        HttpStatusCode.Unauthorized => "Session expired, please log in again.",
        HttpStatusCode.NotFound => "Not found.",
        HttpStatusCode.TooManyRequests => "You're doing that too fast, try again shortly.",
        _ => "Something went wrong. Please try again.",
    };
}
