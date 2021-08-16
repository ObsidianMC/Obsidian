using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class ScaleBias<T> : TransformModule<T, T>
    {
        public T Scale { get; init; }
        public T Bias { get; init; }

        protected override Expression GetTransformExpression(ParameterExpression input)
        {
            return Expression.Add(Expression.Multiply(Source.GetExpression(input), Expression.Constant(Scale)), Expression.Constant(Bias));
        }
    }
}
