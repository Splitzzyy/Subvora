using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SubVora.Infrastructure.Configuration;
using SubVora.Infrastructure.Data;

namespace SubVora.Api.Tests;

/// <summary>
/// Lets the first statement matching <paramref name="sqlFragment"/> commit, then fails the way a
/// connection dropped before the reply does - a transient error, so the execution strategy runs
/// the write again against a database where it already happened.
/// </summary>
internal sealed class LostReplyInterceptor(string sqlFragment) : DbCommandInterceptor
{
    private int _tripped;

    public bool Tripped => _tripped == 1;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (ShouldTrip(command))
        {
            // Drained first, so the connection is left clean for the retry.
            await result.DisposeAsync();
            throw LostReply();
        }

        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default) =>
        ShouldTrip(command) ? throw LostReply() : ValueTask.FromResult(result);

    private bool ShouldTrip(DbCommand command) =>
        command.CommandText.Contains(sqlFragment, StringComparison.Ordinal) && Interlocked.Exchange(ref _tripped, 1) == 0;

    private static NpgsqlException LostReply() => new("Simulated lost reply.", new IOException("Connection reset by peer."));
}

internal static class InterceptorFactoryExtensions
{
    /// <summary>The same app, with <paramref name="interceptor"/> on every context it creates.</summary>
    public static WebApplicationFactory<Program> WithInterceptor(this WebApplicationFactory<Program> factory, IInterceptor interceptor) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddScoped(sp => new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>(AppDbContextOptionsFactory.Build(
                        sp.GetRequiredService<IConfiguration>().GetRequiredConnectionString("Default")))
                    .AddInterceptors(interceptor)
                    .Options));
        }));
}
