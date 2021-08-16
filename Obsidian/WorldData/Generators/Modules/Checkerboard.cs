using System;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Checkerboard : Module<float>
    {
        protected internal override Expression GetExpression(ParameterExpression input)
        {
            if (input is null || input.Type != typeof(Vector3))
                throw new ModuleCompilationException($"Input type for {nameof(Checkerboard)} has to be {nameof(Vector3)}");

            MethodInfo getCheckerboardValue = typeof(Checkerboard).GetMethod(nameof(GetCheckerboardValue));
            return Expression.Call(getCheckerboardValue, input);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetCheckerboardValue(Vector3 vector)
        {
            int ix = (int)MathF.Floor(vector.X) & 1;
            int iy = (int)MathF.Floor(vector.Y) & 1;
            int iz = (int)MathF.Floor(vector.Z) & 1;
            return (ix ^ iy ^ iz) != 0 ? -1f : 1f;
        }
    }
}
