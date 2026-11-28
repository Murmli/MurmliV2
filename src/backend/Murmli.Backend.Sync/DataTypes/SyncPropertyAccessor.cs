using System.Linq.Expressions;
using System.Reflection;

namespace Murmli.Backend.Sync.DataTypes;

internal sealed class SyncPropertyAccessor<TRecord, TValue>
{
    public SyncPropertyAccessor(Expression<Func<TRecord, TValue>> expression)
    {
        var member = SyncPropertyExpressions.GetPropertyAccess(expression);
        var property = (PropertyInfo)member.Member;
        if (!property.CanWrite)
        {
            throw new ArgumentException($"The property {property.Name} has no setter.", nameof(expression));
        }

        var value = Expression.Parameter(typeof(TValue), "value");
        PropertyName = property.Name;
        Get = expression.Compile();
        Set = Expression.Lambda<Action<TRecord, TValue>>(Expression.Assign(member, value), expression.Parameters[0], value).Compile();
    }

    public string PropertyName { get; }

    public Func<TRecord, TValue> Get { get; }

    public Action<TRecord, TValue> Set { get; }
}
