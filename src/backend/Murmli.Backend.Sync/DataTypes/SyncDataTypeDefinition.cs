namespace Murmli.Backend.Sync.DataTypes;

internal sealed record SyncDataTypeDefinition(string Name, string ScopeKind, int MaximumRecordsPerScope, int? MaximumOwnedScopesPerAccount);
