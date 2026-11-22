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
        foreach (var dataType in dataTypes)
        {
            try
            {
                ValidateDataType(dataType, model, dataTypeNames, errors);
            }
            catch (Exception exception)
            {
                errors.Add($"The data type {dataType.GetType().FullName} could not be built or validated ({exception.GetType().Name}).");
            }
        }

        foreach (var (dataTypeName, dataTypeOptions) in options.DataTypes)
        {
            if (!dataTypeNames.Contains(dataTypeName))
            {
                errors.Add($"{SyncOptions.SectionName}:DataTypes:{dataTypeName} names no registered data type.");
            }

            if (dataTypeOptions is { MaxRecordsPerScope: < 1 })
            {
                errors.Add($"{SyncOptions.SectionName}:DataTypes:{dataTypeName}:MaxRecordsPerScope must be at least 1.");
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
        else if (definition.ScopeKind != SyncScope.PersonalKind)
        {
            errors.Add(
                $"The data type '{definition.Name}' must use the scope kind '{SyncScope.PersonalKind}': shared scope kinds need a root type, which arrives with #128."
            );
        }

        errors.AddRange(dataType.Validate(model));
    }
}
