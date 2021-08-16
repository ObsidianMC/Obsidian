using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData.Generators.Modules
{
    public static class ModulesHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowIfNull<T>(Module<T> module)
        {
            if (module is null)
                throw new ModuleCompilationException("Source module was null.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnforceInputType<T>(ParameterExpression input)
        {
            if (!typeof(T).IsAssignableFrom(input.Type))
                throw new ModuleCompilationException($"Input type has to be {typeof(T).Name}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Expression Compress(this Expression expression, ParameterExpression input, string? name = null)
        {
            if (expression.NodeType is not ExpressionType.Block)
            {
                return expression;
            }

            Delegate @delegate = Expression.Lambda(expression, name, new[] { input }).Compile();
            MethodInfo method = @delegate.Method;

            if (method.GetParameters().Length == 0)
            {
                return Expression.Call(method);
            }
            else
            {
                return Expression.Invoke(Expression.Constant(@delegate), input);
            }
        }
    }
}
