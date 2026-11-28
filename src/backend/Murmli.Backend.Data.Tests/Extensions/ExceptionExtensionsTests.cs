using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    public static TheoryData<Exception, bool> LockTimeoutCandidates
    {
        get
        {
            var lockTimeout = new PostgresException(
                "canceling statement due to lock timeout",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.LockNotAvailable
            );
            return new()
            {
                { lockTimeout, true },
                { new RetryLimitExceededException("The maximum number of retries was exceeded.", lockTimeout), true },
                { new DbUpdateException("An error occurred while saving the entity changes.", lockTimeout), true },
                {
                    new PostgresException(
                        "duplicate key value violates unique constraint",
                        "ERROR",
                        "ERROR",
                        PostgresErrorCodes.UniqueViolation
                    ),
                    false
                },
                { new TimeoutException(), false },
            };
        }
    }

    [Theory]
    [MemberData(nameof(InnerExceptions))]
    public void IsDatabaseDisturbance_DbUpdateException_MatchesItsInnerException(Exception? innerException, bool expected) =>
        Assert.Equal(
            expected,
            new DbUpdateException("An error occurred while saving the entity changes.", innerException).IsDatabaseDisturbance()
        );

    [Theory]
    [MemberData(nameof(LockTimeoutCandidates))]
    public void IsLockTimeout_Exception_MatchesLockNotAvailableAtAnyDepth(Exception exception, bool expected) =>
        Assert.Equal(expected, exception.IsLockTimeout());
}
