using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Murmli.Backend.Data.Extensions;

public static class ExceptionExtensions
{
    public static bool IsDatabaseDisturbance(this Exception exception) =>
        exception switch
        {
            RetryLimitExceededException or NpgsqlException { IsTransient: true } or TimeoutException => true,
            DbUpdateException { InnerException: { } innerException } => innerException.IsDatabaseDisturbance(),
            _ => false,
        };

    public static bool IsLockTimeout(this Exception exception) =>
        exception switch
        {
            PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable } => true,
            { InnerException: { } innerException } => innerException.IsLockTimeout(),
            _ => false,
        };
}
