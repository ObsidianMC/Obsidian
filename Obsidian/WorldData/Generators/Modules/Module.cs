using AgileObjects.ReadableExpressions;
using System;
using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Modules
{
    public abstract class Module<TOut>
    {
        protected internal abstract Expression GetExpression(ParameterExpression input);

        public Func<TIn, TOut> Compile<TIn>()
        {
            var parameter = Expression.Parameter(typeof(TIn));
            return Expression.Lambda<Func<TIn, TOut>>(GetExpression(parameter), parameter).Compile();
        }

        public string ToString<TIn>()
        {
            var parameter = Expression.Parameter(typeof(TIn));
            return Expression.Lambda<Func<TIn, TOut>>(GetExpression(parameter), parameter).ToReadableString();
        }
    }
}

//Turbulence
//Perlin
//Cell
//Terrace
//Curve
