using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Constant<T> : Module<T>
    {
        public T Value { get; }

        public Constant(T value)
        {
            Value = value;
        }

        protected internal override Expression GetExpression(ParameterExpression input)
        {
            return Expression.Constant(Value);
        }
    }
}
