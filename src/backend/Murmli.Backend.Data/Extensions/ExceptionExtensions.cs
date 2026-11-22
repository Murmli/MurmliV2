using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Murmli.Backend.Data.Extensions;

public static class ExceptionExtensions
{
    public static bool IsDatabaseDisturbance(this Exception exception) =>
        exception is RetryLimitExceededException or NpgsqlException { IsTransient: true } or TimeoutException;
}
