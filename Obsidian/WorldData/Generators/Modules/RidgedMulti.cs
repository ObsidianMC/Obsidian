using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using static System.Linq.Expressions.Expression;

namespace Obsidian.WorldData.Generators.Modules
{
    public sealed class RidgedMulti : Module<float>
    {
        public float Frequency { get; init; } = 1f;
        public float Lacunarity { get; init; } = 2f;

        /// <summary>
        /// Gets or sets the number of octaves that generate the
        /// ridged-multifractal noise. Ranges 1-30.
        /// </summary>
        public int OctaveCount { get; init; } = 6;
        public int Seed { get; init; }

        protected internal override Expression GetExpression(ParameterExpression input)
        {
            ModulesHelper.EnforceInputType<Vector3>(input);

            if (OctaveCount is < 1 or > 30)
                throw new ArgumentOutOfRangeException(nameof(OctaveCount));

            MethodInfo getCoherentNoise3D = typeof(NoiseHelper).GetMethod(nameof(NoiseHelper.GradientCoherentNoise3D), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            MethodInfo abs = typeof(MathF).GetMethod(nameof(MathF.Abs))!;
            MethodInfo clamp = typeof(Math).GetMethod(nameof(Math.Clamp), new[] { typeof(float), typeof(float), typeof(float) })!;

            ConstantExpression zero = Constant(0f);
            ConstantExpression one = Constant(1f);
            ConstantExpression frequency = Constant(Frequency);
            ConstantExpression lacunarity = Constant(Lacunarity);

            ParameterExpression x = Variable(typeof(float), nameof(x));
            ParameterExpression y = Variable(typeof(float), nameof(y));
            ParameterExpression z = Variable(typeof(float), nameof(z));
            ParameterExpression value = Variable(typeof(float), nameof(value));
            ParameterExpression weight = Variable(typeof(float), nameof(weight));
            ParameterExpression signal = Variable(typeof(float), nameof(signal));

            ParameterExpression[] variables = { x, y, z, value, weight, signal };

            const int LoopSize = 8;
            var body = new List<Expression>(capacity: 8 + OctaveCount * LoopSize);

            // float x = vector.X;
            // float y = vector.Y;
            // float z = vector.Z;
            body.Add(Assign(x, Field(input, nameof(Vector3.X))));
            body.Add(Assign(y, Field(input, nameof(Vector3.Y))));
            body.Add(Assign(z, Field(input, nameof(Vector3.Z))));

            // x *= Frequency;
            // y *= Frequency;
            // z *= Frequency;
            if (Frequency != 1f)
            {
                body.Add(MultiplyAssign(x, frequency));
                body.Add(MultiplyAssign(y, frequency));
                body.Add(MultiplyAssign(z, frequency));
            }

            // weight = 1f;
            body.Add(Assign(weight, one));

            float spectralFrequency = 1f;
            int lastIndex = OctaveCount - 1;
            for (int i = 0; i < OctaveCount; i++)
            {
                var spectralWeight = Constant(MathF.Pow(Frequency, -1f));
                spectralFrequency *= Lacunarity;

                var seed = Constant((Seed + i) & 0x7fffffff);

                // signal = NoiseHelper.GradientCoherentNoise3D(x, y, z, seed);
                body.Add(Assign(signal, Call(getCoherentNoise3D, x, y, z, seed)));

                // signal = 1f - MathF.Abs(signal);
                body.Add(Assign(signal, Subtract(one, Call(abs, signal))));

                // signal *= signal * weight;
                body.Add(MultiplyAssign(signal, Multiply(signal, weight)));

                // weight = Math.Clamp(signal * 2f, 0f, 1f);
                if (i != lastIndex)
                    body.Add(Assign(weight, Call(clamp, Multiply(signal, Constant(2f)), zero, one)));

                // value += signal * spectralWeight;
                if ((float)spectralWeight.Value != 1f)
                    body.Add(AddAssign(value, Multiply(signal, spectralWeight)));
                else
                    body.Add(AddAssign(value, signal));

                // x *= Lacunarity;
                // y *= Lacunarity;
                // z *= Lacunarity;
                if (i != lastIndex)
                {
                    body.Add(MultiplyAssign(x, lacunarity));
                    body.Add(MultiplyAssign(y, lacunarity));
                    body.Add(MultiplyAssign(z, lacunarity));
                }
            }

            // return (value * 1.25f) - 1f;
            body.Add(Subtract(Multiply(value, Constant(1.25f)), one));

            return Block(variables, body);
        }
    }
}
