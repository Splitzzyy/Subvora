using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SubVora.Api;

/// <summary>Catches anything not already handled by a controller's own error responses (ValidationProblem, Conflict, etc.) and returns a generic ProblemDetails instead of the framework's default error page.</summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>
    /// How every foreign key to <c>users</c> is named under the snake-case convention:
    /// <c>fk_{table}_users_user_id</c>. A test walks the model and fails if one is not, so a
    /// renamed constraint breaks CI rather than turning a signed-out write into a 500.
    /// </summary>
    public const string UsersForeignKeySuffix = "_users_user_id";

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

        if (!httpContext.Response.HasStarted && ClientFaultStatus(exception) is { } status)
        {
            _logger.LogDebug(exception, "Request {Method} {Path} answered {Status}.", httpContext.Request.Method, httpContext.Request.Path, status);
            httpContext.Response.StatusCode = status;
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

        // The trace shows which exception failed the request, not only that it returned 500. The
        // type alone: a message can quote an email address (an SMTP reply, a constraint value), and
        // the span leaves the process without the log's masking.
        Activity.Current?.SetTag("error.type", exception.GetType().FullName);

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

    /// <summary>Exceptions that mean the caller, not the server, is at fault - each with the status it deserves instead of a 500.</summary>
    private static int? ClientFaultStatus(Exception exception) => exception switch
    {
        // A request the server could not read carries its own 4xx - 413 when a chunked body runs
        // past the size cap in Program.cs, which Kestrel enforces while the body is being read.
        BadHttpRequestException badRequest => badRequest.StatusCode,

        // An access token outlives its account by up to 15 minutes after DELETE /users/me. A write
        // in that window points at a user row that is gone; the caller is signed out, not hitting a
        // bug, and 401 sends the app down its existing sign-out path. SaveChanges wraps the
        // database error; ExecuteUpdate and raw SQL throw it bare.
        DbUpdateException { InnerException: PostgresException postgres } when IsMissingUser(postgres) => StatusCodes.Status401Unauthorized,
        PostgresException postgres when IsMissingUser(postgres) => StatusCodes.Status401Unauthorized,

        _ => null,
    };

    private static bool IsMissingUser(PostgresException exception) =>
        exception is { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: { } constraint }
        && constraint.EndsWith(UsersForeignKeySuffix, StringComparison.Ordinal);
}
