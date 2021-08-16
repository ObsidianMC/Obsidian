using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Max<TIn, TOut> : CombineModule<TIn, TOut>
    {
        protected override Expression GetCombineExpression(Expression input1, Expression input2, ParameterExpression input)
        {
            MethodInfo mathMin = typeof(Math).GetMethod(nameof(Math.Max), new[] { typeof(TIn), typeof(TIn) });
            return Expression.Call(mathMin, input1, input2);
        }
    }
}
