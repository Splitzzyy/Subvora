using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SubVora.Api;

/// <summary>Catches anything not already handled by a controller's own error responses (ValidationProblem, Conflict, etc.) and returns a generic ProblemDetails instead of the framework's default error page.</summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // A client that backgrounds the app or navigates away aborts the request, and the resulting
        // OperationCanceledException arrives here. That is routine mobile traffic, not a server
        // fault: logging it at Error makes the error rate meaningless, and there is no longer a
        // connection to write a 500 to. Every background service already filters this the same way.
        if (httpContext.RequestAborted.IsCancellationRequested && exception is OperationCanceledException)
        {
            _logger.LogDebug("Request {Method} {Path} was aborted by the client.", httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        // Ties the response a user can quote back to the log line that explains it. Activity.Id is
        // the W3C trace parent when one is flowing; TraceIdentifier is the per-connection fallback.
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        _logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path} ({TraceId})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            traceId);

        // Nothing can be written once the response is on the wire - assigning StatusCode below would
        // throw and replace a partial response with a second, less legible failure. Returning false
        // hands it back to the server, which aborts the connection so the client sees a truncated
        // response rather than a silently complete one.
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Extensions = { ["traceId"] = traceId },
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json", cancellationToken);

        return true;
    }
}
