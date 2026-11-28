using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Murmli.Backend.Sync.Configuration;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class SyncDataTypeRegistry
{
    private readonly Dictionary<string, SyncDataType> dataTypesByName = new(StringComparer.Ordinal);

    private readonly Dictionary<SyncDataType, int> maximumRecordsPerScope = [];

    private readonly Dictionary<string, int> deletionThresholds = new(StringComparer.Ordinal);

    public SyncDataTypeRegistry(IEnumerable<SyncDataType> dataTypes, IOptions<SyncOptions> options)
    {
        // Reading the options first reports the start validation errors before a broken definition throws
        var dataTypeOptions = options.Value.DataTypes;
        All = [.. dataTypes];
        foreach (var dataType in All)
        {
            dataTypesByName.Add(dataType.Name, dataType);
            var maximum = dataTypeOptions.GetValueOrDefault(dataType.Name)?.MaxRecordsPerScope ?? dataType.Definition.MaximumRecordsPerScope;
            maximumRecordsPerScope.Add(dataType, maximum);
            deletionThresholds[dataType.ScopeKind] = (int)Math.Min((long)deletionThresholds.GetValueOrDefault(dataType.ScopeKind) + maximum, int.MaxValue);
        }
    }

    public IReadOnlyList<SyncDataType> All { get; }

    public bool TryGet(string name, [NotNullWhen(true)] out SyncDataType? dataType) => dataTypesByName.TryGetValue(name, out dataType);

    public IReadOnlyList<SyncDataType> GetListed(IReadOnlyList<string> dataTypes)
    {
        var listedNames = dataTypes.ToHashSet(StringComparer.Ordinal);
        return [.. All.Where(dataType => listedNames.Contains(dataType.Name))];
    }

    public int GetMaximumRecordsPerScope(SyncDataType dataType) => maximumRecordsPerScope[dataType];

    public int GetDeletionThreshold(string scopeKind) => deletionThresholds.GetValueOrDefault(scopeKind);
}
