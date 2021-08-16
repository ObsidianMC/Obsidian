using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Modules
{
    public abstract class TransformModule<TIn, TOut> : Module<TOut>
    {
        public Module<TIn> Source { get; init; }

        protected abstract Expression GetTransformExpression(ParameterExpression input);

        protected internal override Expression GetExpression(ParameterExpression input)
        {
            ModulesHelper.ThrowIfNull(Source);
            return GetTransformExpression(input);
        }
    }
}
