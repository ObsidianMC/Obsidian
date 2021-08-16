using System;
using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Modules
{
    public abstract class StatefulModule<TIn, TOut> : Module<TOut>
    {
        protected abstract TOut GetValue(TIn input);

        protected virtual void InitializeState()
        {
        }

        protected internal sealed override Expression GetExpression(ParameterExpression input)
        {
            InitializeState();

            Func<TIn, TOut> getValue = GetValue;
            return Expression.Call(Expression.Constant(this), getValue.Method, input);
        }
    }
}
