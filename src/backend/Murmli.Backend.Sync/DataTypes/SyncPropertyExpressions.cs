using System.Linq.Expressions;
using System.Reflection;

namespace Murmli.Backend.Sync.DataTypes;

internal static class SyncPropertyExpressions
{
    public static MemberExpression GetPropertyAccess(LambdaExpression expression) =>
        expression.Body is MemberExpression { Member: PropertyInfo, Expression: ParameterExpression parameter } member
        && parameter == expression.Parameters[0]
            ? member
            : throw new ArgumentException($"The expression {expression} must access a property of its parameter directly.", nameof(expression));
}
