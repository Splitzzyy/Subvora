using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SubVora.Infrastructure.Data;

public static class DbUpdateExceptionExtensions
{
    /// <summary>
    /// Npgsql surfaces a unique-index violation as SQLSTATE 23505. Matching on the state code
    /// rather than the message keeps this working across locales and server versions.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
