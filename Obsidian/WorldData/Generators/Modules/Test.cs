using System.Linq.Expressions;
using System.Numerics;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Test : Module<float>
    {
        protected internal override Expression GetExpression(ParameterExpression input)
        {
            return Expression.Block(Expression.Field(input, nameof(Vector3.Z)));
        }
    }
}
