using Murmli.Backend.Sync.Writing;

namespace Murmli.Backend.Sync.Accounts;

public interface ISyncAccountBootstrapContributor
{
    IReadOnlyList<SyncServerCommand> CreateCommands(Guid accountId);
}
