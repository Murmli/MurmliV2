using System.Linq.Expressions;
using System.Reflection;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class SyncPropertyAccessor<TRecord, TValue>
{
    public SyncPropertyAccessor(Expression<Func<TRecord, TValue>> expression)
    {
        if (
            expression.Body is not MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression parameter } member
            || parameter != expression.Parameters[0]
        )
        {
            throw new ArgumentException($"The expression {expression} must access a property of its parameter directly.", nameof(expression));
        }

        if (!property.CanWrite)
        {
            throw new ArgumentException($"The property {property.Name} has no setter.", nameof(expression));
        }

        var value = Expression.Parameter(typeof(TValue), "value");
        PropertyName = property.Name;
        Get = expression.Compile();
        Set = Expression.Lambda<Action<TRecord, TValue>>(Expression.Assign(member, value), parameter, value).Compile();
    }

    public string PropertyName { get; }

    public Func<TRecord, TValue> Get { get; }

    public Action<TRecord, TValue> Set { get; }
}
