using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Clamp<T> : TransformModule<T, T> where T : IComparable<T>
    {
        public T LowerBound { get; init; }
        public T UpperBound { get; init; }

        protected override Expression GetTransformExpression(ParameterExpression input)
        {
            if (LowerBound.CompareTo(UpperBound) > 1)
                throw new ModuleCompilationException("LowerBound must be lower than UpperBound.");

            MethodInfo? clampMethod = typeof(Math).GetMethod(nameof(Math.Clamp), new[] { typeof(T), typeof(T), typeof(T) });

            if (clampMethod is null)
            {
                // Compare via IComparable<T>
                clampMethod = typeof(Clamp<T>).GetMethod(nameof(ClampGeneric))!;
            }

            return Expression.Call(clampMethod, Source.GetExpression(input), Expression.Constant(LowerBound), Expression.Constant(UpperBound));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T ClampGeneric<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
            {
                return min;
            }
            else if (value.CompareTo(max) > 0)
            {
                return max;
            }
            else
            {
                return value;
            }
        }
    }
}
