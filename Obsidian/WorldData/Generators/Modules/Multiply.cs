using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Multiply<TIn, TOut> : CombineModule<TIn, TOut>
    {
        protected override Expression GetCombineExpression(Expression input1, Expression input2, ParameterExpression input)
        {
            Type type1 = input1.Type;
            Type type2 = input2.Type;
            MethodInfo? multiplyOverload = type1.GetMethod("op_Multiply", new[] { type1, type2 });

            // Custom multiply operator overload
            if (multiplyOverload is not null)
            {
                return Expression.Call(multiplyOverload, input1, input2);
            }

            // Arithmetic multiply/Failure
            return Expression.Multiply(input1, input2);
        }
    }
}
