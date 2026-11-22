using Microsoft.EntityFrameworkCore;

namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncCreateRaceException(DbUpdateException innerException)
    : Exception("Another scope created a record with the same id while the sync command was processed.", innerException);
