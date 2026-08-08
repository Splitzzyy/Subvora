using System.Net;
using Refit;

namespace SubVora.Mobile.Services;

/// <summary>
/// Centralized API-error-to-display-message mapping, reused by every ViewModel's catch block
/// instead of ad hoc per-screen string handling.
/// </summary>
public static class ApiErrorMapper
{
    /// <summary>
    /// Whether this is a way an API call can fail, as opposed to a bug in our own code.
    /// <para>
    /// Refit raises <see cref="ApiException"/> only when the server actually answered with an error
    /// status. When the API cannot be reached at all - stopped container, dead adb tunnel, no
    /// network - there is no response to wrap, so HttpClient's own
    /// <see cref="HttpRequestException"/> (connection refused, no route) or
    /// <see cref="TaskCanceledException"/> (connect timeout) comes through untouched. Catching only
    /// ApiException therefore let those escape the command and take the app down.
    /// </para>
    /// <para>
    /// Used as a catch filter rather than catching Exception outright: a NullReferenceException is
    /// a defect and should surface as one, not be reported to the user as a network problem.
    /// </para>
    /// </summary>
    public static bool IsApiFailure(Exception exception) =>
        exception is ApiException or HttpRequestException or TaskCanceledException;

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
