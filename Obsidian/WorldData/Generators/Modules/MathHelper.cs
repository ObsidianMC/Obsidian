using System.Runtime.CompilerServices;

namespace Obsidian.WorldData.Generators.Modules
{
    public static class MathHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SCurve3(float f)
        {
            return f * f * (3f - 2f * f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SCurve5(float f)
        {
            return f * f * f * (f * (6f * f - 15f) + 10f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float alpha)
        {
            return a + (b - a) * alpha;
        }
    }
}
