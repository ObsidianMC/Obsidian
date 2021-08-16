using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Add<TIn, TOut> : CombineModule<TIn, TOut>
    {
        protected override Expression GetCombineExpression(Expression input1, Expression input2, ParameterExpression input)
        {
            Type type1 = input1.Type;
            Type type2 = input2.Type;
            MethodInfo? addOverload = type1.GetMethod("op_Addition", new[] { type1, type2 });

            // Custom add operator overload
            if (addOverload is not null)
            {
                return Expression.Call(addOverload, input1, input2);
            }

            // Arithmetic add/Failure
            return Expression.Add(input1, input2);
        }
    }
}
