using System.Linq.Expressions;
using System.Reflection;
using Murmli.Backend.Data.Sync;

namespace Murmli.Backend.Sync.DataTypes;

public sealed class SyncRuleContext<TRecord>
    where TRecord : SyncRecord
{
    private readonly SyncRuleInput input;

    internal SyncRuleContext(TRecord record, SyncRuleInput input)
    {
        Record = record;
        this.input = input;
    }

    public TRecord Record { get; }

    public Guid? ActingMemberId => input.ActingMemberId;

    public DateTimeOffset ServerTime => input.ServerTime;

    public bool Applied<TValue>(Expression<Func<TRecord, TValue>> field) => input.AppliedProperties.Contains(GetPropertyName(field));

    public TValue Original<TValue>(Expression<Func<TRecord, TValue>> field)
    {
        var propertyName = GetPropertyName(field);
        return input.ReadOriginal is { } readOriginal ? (TValue)readOriginal(propertyName)! : default!;
    }

    private static string GetPropertyName(LambdaExpression field) =>
        ((PropertyInfo)SyncPropertyExpressions.GetPropertyAccess(field).Member).Name;
}
