using System;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class Billow : Module<float>
    {
        public float Frequency { get; init; } = 1f;
        public float Lacunarity { get; init; } = 2f;
        public int OctaveCount { get; init; } = 6;
        public float Persistance { get; init; } = 0.5f;
        public int Seed { get; init; }

        protected internal override Expression GetExpression(ParameterExpression input)
        {
            ModulesHelper.EnforceInputType<Vector3>(input);

            MethodInfo getBillowNoise = typeof(Billow).GetMethod(nameof(GetBillowNoise), BindingFlags.NonPublic | BindingFlags.Static)!;

            return Expression.Call(getBillowNoise,
                input,
                Expression.Constant(Frequency),
                Expression.Constant(Lacunarity),
                Expression.Constant(OctaveCount),
                Expression.Constant(Persistance),
                Expression.Constant(Seed));
        }

        private static float GetBillowNoise(Vector3 vector, float frequency, float lacunarity, int octaveCount, float persistance, int seed)
        {
            float value = 0.5f;
            float currentPersistance = 1f;

            float x = vector.X * frequency;
            float y = vector.Y * frequency;
            float z = vector.Z * frequency;

            for (int i = 0; i < octaveCount; i++)
            {
                float signal = NoiseHelper.GradientCoherentNoise3D(x, y, z, (seed + i) & int.MaxValue);
                signal = MathF.Abs(signal) - 1.5f;
                value += signal * currentPersistance;

                x *= lacunarity;
                y *= lacunarity;
                z *= lacunarity;
                currentPersistance *= persistance;
            }

            return value;
        }
    }
}
