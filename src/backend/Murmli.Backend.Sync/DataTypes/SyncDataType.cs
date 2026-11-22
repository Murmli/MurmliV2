using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Extensions;

namespace Murmli.Backend.Sync.DataTypes;

public abstract class SyncDataType
{
    private protected SyncDataType() { }

    public string Name => Definition.Name;

    public string ScopeKind => Definition.ScopeKind;

    internal abstract SyncDataTypeDefinition Definition { get; }

    internal abstract IReadOnlyList<string> Validate(IModel model);

    internal abstract SyncFieldChanges? ParseFields(JsonElement fields, SyncCommandKind kind);

    internal abstract Task<SyncRecord?> FindTrackedAsync(MurmliDbContext context, Guid recordId, CancellationToken cancellationToken);

    internal abstract Task<int> CountInScopeAsync(MurmliDbContext context, Guid scopeId, CancellationToken cancellationToken);

    internal abstract SyncRecord CreateRecord(Guid recordId, Guid scopeId);

    internal abstract bool ApplyChanges(SyncRecord record, SyncFieldChanges changes, DateTimeOffset commandTime, bool isNew);

    internal abstract SyncRuleDecision EvaluateRules(SyncRecord record);

    internal abstract string GetPrimaryKeyName(MurmliDbContext context);

    internal abstract Task WriteRecordsAsync(
        MurmliDbContext context,
        Guid scopeId,
        long afterVersion,
        Guid[] includedIds,
        Utf8JsonWriter writer,
        ISet<Guid> writtenIds,
        CancellationToken cancellationToken
    );
}

public abstract class SyncDataType<TRecord> : SyncDataType
    where TRecord : SyncRecord, new()
{
    internal sealed override SyncDataTypeDefinition Definition => Layout.Definition;

    private SyncDataTypeLayout<TRecord> Layout => field ??= BuildLayout();

    private string DisplayName => $"'{Name}' ({GetType().Name})";

    protected abstract void Define(SyncDataTypeBuilder<TRecord> type);

    protected virtual SyncRuleDecision Evaluate(SyncRuleContext<TRecord> context) => SyncRuleDecision.Continue;

    internal sealed override IReadOnlyList<string> Validate(IModel model)
    {
        var errors = new List<string>();
        if (Definition.MaxRecordsPerScope < 1)
        {
            errors.Add($"The data type {DisplayName} must call MaxRecordsPerScope with a value of at least 1.");
        }

        if (Layout.Groups.Count == 0)
        {
            errors.Add($"The data type {DisplayName} declares no field group.");
        }

        foreach (var fieldName in GetDuplicates(Layout.Fields.Select(field => field.Name)))
        {
            errors.Add($"The data type {DisplayName} declares the field '{fieldName}' more than once.");
        }

        var boundProperties = Layout
            .Fields.Select(field => field.PropertyName)
            .Concat(Layout.Groups.Select(group => group.ChangedAtPropertyName));
        foreach (var propertyName in GetDuplicates(boundProperties))
        {
            errors.Add($"The data type {DisplayName} binds the property {propertyName} more than once.");
        }

        foreach (var field in Layout.Fields.Where(field => !field.HasDefaultOnCreate && !field.IsRequiredOnCreate))
        {
            errors.Add($"The field '{field.Name}' of the data type {DisplayName} has no default on create, so it must be RequiredOnCreate.");
        }

        var entityType = model.FindEntityType(typeof(TRecord));
        if (entityType is null)
        {
            errors.Add($"The entity type {typeof(TRecord).Name} of the data type {DisplayName} is not mapped in the model.");
            return errors;
        }

        foreach (var field in Layout.Fields)
        {
            var property = entityType.FindProperty(field.PropertyName);
            if (property is null)
            {
                errors.Add($"The property {field.PropertyName} of the field '{field.Name}' of the data type {DisplayName} is not mapped in the model.");
            }
            else if (property.IsNullable != field.IsNullable)
            {
                errors.Add(
                    $"The field '{field.Name}' of the data type {DisplayName} is {(field.IsNullable ? "nullable" : "not nullable")}, but its property {field.PropertyName} is {(property.IsNullable ? "nullable" : "required")} in the model."
                );
            }
        }

        foreach (var group in Layout.Groups)
        {
            var property = entityType.FindProperty(group.ChangedAtPropertyName);
            if (property is null)
            {
                errors.Add(
                    $"The changed-at property {group.ChangedAtPropertyName} of the group '{group.Name}' of the data type {DisplayName} is not mapped in the model."
                );
            }
            else if (property.IsNullable)
            {
                errors.Add(
                    $"The changed-at property {group.ChangedAtPropertyName} of the group '{group.Name}' of the data type {DisplayName} must not be nullable in the model."
                );
            }
        }

        return errors;
    }

    internal sealed override SyncFieldChanges? ParseFields(JsonElement fields, SyncCommandKind kind)
    {
        if (fields.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var assignments = new Dictionary<SyncField<TRecord>, Action<TRecord>>();
        foreach (var member in fields.EnumerateObject())
        {
            if (
                !member.TryGetReadableName(out var name)
                || !Layout.DeviceFields.TryGetValue(name, out var field)
                || assignments.ContainsKey(field)
                || !field.TryRead(member.Value, out var assignment)
            )
            {
                return null;
            }

            assignments.Add(field, assignment);
        }

        if (kind == SyncCommandKind.SetFields && assignments.Count == 0)
        {
            return null;
        }

        if (kind == SyncCommandKind.Create && !Layout.RequiredOnCreateFields.All(assignments.ContainsKey))
        {
            return null;
        }

        var groupChanges = new List<SyncGroupChange<TRecord>>();
        foreach (var group in Layout.Groups)
        {
            var namedFieldCount = group.Fields.Count(assignments.ContainsKey);
            if (namedFieldCount == 0)
            {
                continue;
            }

            if (namedFieldCount != group.Fields.Count)
            {
                return null;
            }

            groupChanges.Add(new SyncGroupChange<TRecord>(group, [.. group.Fields.Select(field => assignments[field])]));
        }

        return new SyncFieldChanges<TRecord>(groupChanges);
    }

    internal sealed override async Task<SyncRecord?> FindTrackedAsync(
        MurmliDbContext context,
        Guid recordId,
        CancellationToken cancellationToken
    ) => await context.Set<TRecord>().AsTracking().SingleOrDefaultAsync(record => record.Id == recordId, cancellationToken);

    internal sealed override Task<int> CountInScopeAsync(MurmliDbContext context, Guid scopeId, CancellationToken cancellationToken) =>
        context.Set<TRecord>().CountAsync(record => record.ScopeId == scopeId, cancellationToken);

    internal sealed override SyncRecord CreateRecord(Guid recordId, Guid scopeId)
    {
        var record = new TRecord { Id = recordId, ScopeId = scopeId };
        foreach (var field in Layout.Fields)
        {
            field.SetDefault(record);
        }

        return record;
    }

    internal sealed override bool ApplyChanges(SyncRecord record, SyncFieldChanges changes, DateTimeOffset commandTime, bool isNew)
    {
        var typedRecord = (TRecord)record;
        var applied = false;
        foreach (var change in ((SyncFieldChanges<TRecord>)changes).Groups)
        {
            if (isNew || commandTime >= change.Group.GetChangedAt(typedRecord))
            {
                foreach (var assignment in change.Assignments)
                {
                    assignment(typedRecord);
                }

                change.Group.SetChangedAt(typedRecord, commandTime);
                applied = true;
            }
        }

        if (!isNew)
        {
            return applied;
        }

        foreach (var group in Layout.Groups)
        {
            group.SetChangedAt(typedRecord, commandTime);
        }

        return true;
    }

    internal sealed override SyncRuleDecision EvaluateRules(SyncRecord record) => Evaluate(new SyncRuleContext<TRecord>((TRecord)record));

    internal sealed override string GetPrimaryKeyName(MurmliDbContext context) =>
        context.Model.FindEntityType(typeof(TRecord))!.FindPrimaryKey()!.GetName()!;

    internal sealed override async Task WriteRecordsAsync(
        MurmliDbContext context,
        Guid scopeId,
        long afterVersion,
        Guid[] includedIds,
        Utf8JsonWriter writer,
        ISet<Guid> writtenIds,
        CancellationToken cancellationToken
    )
    {
        var records = context
            .Set<TRecord>()
            .Where(record => record.ScopeId == scopeId && (record.Version > afterVersion || includedIds.Contains(record.Id)))
            .OrderBy(record => record.Id)
            .AsAsyncEnumerable();
        await foreach (var record in records.WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("dataType", Name);
            writer.WriteString("id", record.Id);
            writer.WriteStartObject("fields");
            foreach (var field in Layout.SentFields)
            {
                field.Write(writer, record);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writtenIds.Add(record.Id);
        }
    }

    private static IEnumerable<string> GetDuplicates(IEnumerable<string> names) =>
        names.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key);

    private SyncDataTypeLayout<TRecord> BuildLayout()
    {
        var builder = new SyncDataTypeBuilder<TRecord>();
        Define(builder);
        return builder.Build();
    }
}
