using System.Net;
using System.Text;
using Refit;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>Builds a real Refit ApiException for tests that need to simulate a failed call
/// through a plain (non-IApiResponse) Refit interface method.</summary>
public static class TestApiExceptions
{
    public static ApiException Create(HttpStatusCode statusCode, string? errorJson = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://test.local/"),
        };

        if (errorJson is not null)
        {
            response.Content = new StringContent(errorJson, Encoding.UTF8, "application/json");
        }

        return ApiException.Create(
            new HttpRequestMessage(HttpMethod.Post, "https://test.local/"),
            HttpMethod.Post,
            response,
            new RefitSettings()).GetAwaiter().GetResult();
    }

    /// <summary>
    /// What Refit 13 actually throws when the server cannot be reached at all: the underlying
    /// HttpRequestException wrapped, with no response and no status code. Built as the real type
    /// rather than a stand-in, because the bug it guards was precisely that this type is not an
    /// <see cref="ApiException"/> and so slipped through the catch filters.
    /// </summary>
    public static ApiRequestException ConnectionFailure() =>
        new(
            "Connection failure",
            new HttpRequestMessage(HttpMethod.Get, "https://test.local/"),
            HttpMethod.Get,
            new RefitSettings(),
            new HttpRequestException("No route to host"));
}
