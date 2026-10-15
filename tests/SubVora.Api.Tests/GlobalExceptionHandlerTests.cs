using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SubVora.Api;
using SubVora.Domain.Entities;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_WritesProblemDetailsResponse()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("secret internal detail"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.DoesNotContain("secret internal detail", body);
        Assert.Contains("An unexpected error occurred.", body);
    }

    [Fact]
    public async Task TryHandleAsync_ABodyOverTheSizeCap_Answers413()
    {
        // Kestrel enforces the cap on a chunked body while it is read, by throwing; that is the
        // caller's fault, not a 500.
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(httpContext, new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, httpContext.Response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryHandleAsync_AWriteForADeletedAccount_Answers401(bool wrappedBySaveChanges)
    {
        // SaveChanges wraps the database error; ExecuteUpdate and raw SQL surface it bare.
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var foreignKeyViolation = new PostgresException(
            "insert or update violates foreign key constraint", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation,
            constraintName: "fk_user_subscriptions" + GlobalExceptionHandler.UsersForeignKeySuffix);
        Exception exception = wrappedBySaveChanges ? new DbUpdateException("save failed", foreignKeyViolation) : foreignKeyViolation;

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public void EveryForeignKeyToUsers_CascadesAndCarriesTheNameTheHandlerMatches()
    {
        // The handler's 401 for a deleted account's write, and account deletion itself, both depend
        // on every user-owned table: one by the constraint name, the other by ON DELETE CASCADE.
        // Building the model needs no database.
        using var db = new AppDbContext(AppDbContextOptionsFactory.Build("Host=localhost"));
        var foreignKeys = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(User))
            .ToList();

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, fk =>
        {
            Assert.EndsWith(GlobalExceptionHandler.UsersForeignKeySuffix, fk.GetConstraintName());
            Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
        });
    }

    [Fact]
    public async Task TryHandleAsync_IncludesTraceIdTheCallerCanQuote()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-id-under-test" };
        httpContext.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(httpContext, new InvalidOperationException(), CancellationToken.None);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);

        // Activity.Current wins when one is flowing (it is not, here), so this asserts the fallback
        // is present and non-empty rather than pinning the exact source.
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
    }

    [Fact]
    public async Task TryHandleAsync_WhenClientAborted_HandlesQuietlyWithoutWriting()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        var httpContext = new DefaultHttpContext { RequestAborted = aborted.Token };
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(httpContext, new OperationCanceledException(), CancellationToken.None);

        Assert.True(handled);
        // Untouched: a disconnected client is not a 500, and there is nothing left to write to.
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    [Fact]
    public async Task TryHandleAsync_WhenResponseAlreadyStarted_DefersToTheServer()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException(), CancellationToken.None);

        // False, so the server aborts the connection - writing here would throw on StatusCode.
        Assert.False(handled);
    }

    /// <summary>A response that is already on the wire. DefaultHttpContext's own feature always reports HasStarted false.</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
