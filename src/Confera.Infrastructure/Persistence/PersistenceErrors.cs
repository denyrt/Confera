using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Confera.Infrastructure.Persistence;

internal static class PersistenceErrors
{
    internal static Exception Unwrap(Exception error)
    {
        // The non-retrying provider strategy and SaveChanges wrap query/provider errors.
        while (error is DbUpdateException or InvalidOperationException && error.InnerException is { } inner)
        {
            error = inner;
        }

        return error;
    }

    internal static bool IsUnavailable(Exception error) =>
        error is NpgsqlException { IsTransient: true }
            or PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled }
            or TimeoutException;
}
