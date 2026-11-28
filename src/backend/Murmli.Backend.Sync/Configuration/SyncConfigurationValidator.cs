using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.DataTypes;

namespace Murmli.Backend.Sync.Configuration;

internal sealed class SyncConfigurationValidator(IEnumerable<SyncDataType> dataTypes, IServiceScopeFactory scopeFactory)
    : IValidateOptions<SyncOptions>
{
    public ValidateOptionsResult Validate(string? name, SyncOptions options)
    {
        using var scope = scopeFactory.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<MurmliDbContext>().Model;
        var errors = new List<string>();
        var dataTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new List<SyncDataTypeDefinition>();
        foreach (var dataType in dataTypes)
        {
            try
            {
                definitions.Add(dataType.Definition);
                ValidateDataType(dataType, model, dataTypeNames, errors);
            }
            catch (Exception exception)
            {
                errors.Add($"The data type {dataType.GetType().FullName} could not be built or validated ({exception.GetType().Name}).");
            }
        }

        ValidateScopeRoots(definitions, errors);
        foreach (var (dataTypeName, dataTypeOptions) in options.DataTypes)
        {
            if (!dataTypeNames.Contains(dataTypeName))
            {
                errors.Add($"{SyncOptions.SectionName}:DataTypes:{dataTypeName} names no registered data type.");
            }
            else if (
                dataTypeOptions is { MaxOwnedScopesPerAccount: not null }
                && !definitions.Any(definition => definition.Name == dataTypeName && definition.MaximumOwnedScopesPerAccount is not null)
            )
            {
                errors.Add($"{SyncOptions.SectionName}:DataTypes:{dataTypeName}:MaxOwnedScopesPerAccount applies only to scope root types.");
            }

            if (dataTypeOptions is { MaxRecordsPerScope: < 1 })
            {
                errors.Add($"{SyncOptions.SectionName}:DataTypes:{dataTypeName}:MaxRecordsPerScope must be at least 1.");
            }

            if (dataTypeOptions is { MaxOwnedScopesPerAccount: < 1 })
            {
                errors.Add($"{SyncOptions.SectionName}:DataTypes:{dataTypeName}:MaxOwnedScopesPerAccount must be at least 1.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateDataType(SyncDataType dataType, IModel model, HashSet<string> dataTypeNames, List<string> errors)
    {
        var definition = dataType.Definition;
        if (string.IsNullOrEmpty(definition.Name))
        {
            errors.Add($"The data type {dataType.GetType().FullName} must call Named with a non-empty name.");
        }
        else if (!dataTypeNames.Add(definition.Name))
        {
            errors.Add($"The data type name '{definition.Name}' is registered more than once.");
        }

        if (string.IsNullOrEmpty(definition.ScopeKind))
        {
            errors.Add($"The data type {dataType.GetType().FullName} must call Named with a non-empty scope kind.");
        }

        errors.AddRange(dataType.Validate(model));
    }

    private static void ValidateScopeRoots(List<SyncDataTypeDefinition> definitions, List<string> errors)
    {
        foreach (
            var kind in definitions
                .Where(definition => !string.IsNullOrEmpty(definition.ScopeKind))
                .GroupBy(definition => definition.ScopeKind, StringComparer.Ordinal)
        )
        {
            var rootNames = kind
                .Where(definition => definition.MaximumOwnedScopesPerAccount is not null)
                .Select(definition => definition.Name)
                .ToList();
            if (kind.Key == SyncScope.PersonalKind)
            {
                errors.AddRange(
                    rootNames.Select(rootName => $"The data type '{rootName}' is a scope root of the scope kind '{SyncScope.PersonalKind}', which has no roots.")
                );
            }
            else if (rootNames.Count == 0)
            {
                errors.Add($"The scope kind '{kind.Key}' has no scope root type.");
            }
            else if (rootNames.Count > 1)
            {
                errors.Add(
                    $"The scope kind '{kind.Key}' has more than one scope root type: {string.Join(", ", rootNames.Select(rootName => $"'{rootName}'"))}."
                );
            }
        }
    }
}
