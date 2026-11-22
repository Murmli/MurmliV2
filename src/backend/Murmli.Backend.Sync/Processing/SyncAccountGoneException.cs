using Npgsql;

namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncAccountGoneException(PostgresException innerException)
    : Exception("The account was deleted while its sync command was processed.", innerException);
