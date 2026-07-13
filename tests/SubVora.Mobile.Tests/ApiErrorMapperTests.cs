using System.Net;
using SubVora.Mobile.Services;
using SubVora.Mobile.Tests.Fakes;

namespace SubVora.Mobile.Tests;

public class ApiErrorMapperTests
{
    [Fact]
    public void ToDisplayMessage_With400ValidationProblemDetails_MapsToFieldLevelMessage()
    {
        var exception = TestApiExceptions.Create(
            HttpStatusCode.BadRequest,
            """{"errors":{"Email":["'Email' is not a valid email address."]}}""");

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("'Email' is not a valid email address.", message);
    }

    [Fact]
    public void ToDisplayMessage_With401_MapsToSessionExpiredMessage()
    {
        var exception = TestApiExceptions.Create(HttpStatusCode.Unauthorized);

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("Session expired, please log in again.", message);
    }

    [Fact]
    public void ToDisplayMessage_With404_MapsToGenericNotFoundMessage()
    {
        var exception = TestApiExceptions.Create(HttpStatusCode.NotFound);

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("Not found.", message);
    }

    [Fact]
    public void ToDisplayMessage_With429_MapsToRateLimitMessage()
    {
        var exception = TestApiExceptions.Create(HttpStatusCode.TooManyRequests);

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("You're doing that too fast, try again shortly.", message);
    }

    [Fact]
    public void ToDisplayMessage_WithNetworkFailure_MapsToOfflineMessage()
    {
        var exception = new HttpRequestException("No such host is known.");

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("You appear to be offline.", message);
    }

    [Fact]
    public void ToDisplayMessage_WithTimeout_MapsToOfflineMessage()
    {
        var exception = new TaskCanceledException("The operation was canceled.");

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("You appear to be offline.", message);
    }

    [Fact]
    public void ToDisplayMessage_WithUnmappedStatusCode_MapsToGenericMessage()
    {
        var exception = TestApiExceptions.Create(HttpStatusCode.InternalServerError);

        var message = ApiErrorMapper.ToDisplayMessage(exception);

        Assert.Equal("Something went wrong. Please try again.", message);
    }
}
