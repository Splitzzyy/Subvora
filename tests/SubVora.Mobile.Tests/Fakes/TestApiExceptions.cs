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
}
