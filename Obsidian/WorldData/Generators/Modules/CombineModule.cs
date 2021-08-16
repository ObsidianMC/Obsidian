using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Modules
{
    public abstract class CombineModule<TIn, TOut> : Module<TOut>
    {
        public Module<TIn> Source0 { get; init; }
        public Module<TIn> Source1 { get; init; }

        protected abstract Expression GetCombineExpression(Expression input1, Expression input2, ParameterExpression input);

        protected internal sealed override Expression GetExpression(ParameterExpression input)
        {
            ModulesHelper.ThrowIfNull(Source0);
            ModulesHelper.ThrowIfNull(Source1);

            return GetCombineExpression(Source0.GetExpression(input), Source1.GetExpression(input), input);
        }
    }
}
