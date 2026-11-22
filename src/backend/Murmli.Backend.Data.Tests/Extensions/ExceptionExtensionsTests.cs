using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data.Extensions;
using Npgsql;

namespace Murmli.Backend.Data.Tests.Extensions;

public class ExceptionExtensionsTests
{
    public static TheoryData<Exception?, bool> InnerExceptions =>
        new()
        {
            { new NpgsqlException("Exception while reading from stream", new TimeoutException()), true },
            { new TimeoutException(), true },
            {
                new PostgresException(
                    "duplicate key value violates unique constraint",
                    "ERROR",
                    "ERROR",
                    PostgresErrorCodes.UniqueViolation
                ),
                false
            },
            { null, false },
        };

    [Theory]
    [MemberData(nameof(InnerExceptions))]
    public void IsDatabaseDisturbance_DbUpdateException_MatchesItsInnerException(Exception? innerException, bool expected) =>
        Assert.Equal(
            expected,
            new DbUpdateException("An error occurred while saving the entity changes.", innerException).IsDatabaseDisturbance()
        );
}
