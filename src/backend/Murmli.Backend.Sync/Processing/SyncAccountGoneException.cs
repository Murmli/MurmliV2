namespace Murmli.Backend.Sync.Processing;

internal sealed class SyncAccountGoneException(Exception? innerException = null)
    : Exception("The account was deleted while its sync command was processed.", innerException);
