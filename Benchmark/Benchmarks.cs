using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Mathematics;
using Obsidian.WorldData.Generators.Modules;
using SharpNoise;
using SharpNoise.Modules;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Benchmark
{
    [DisassemblyDiagnoser]
    public class Benchmarks
    {
        private readonly Module sharpNoiseModule;
        private readonly Module<float> obsidianModule;
        private readonly Func<Vector3, float> obsidianFunc;

        public IEnumerable<object[]> DoubleArgs => new object[][] { new object[] { 1d, 2d, 3d } };
        public IEnumerable<object[]> FloatArgs => new object[][] { new object[] { 1f, 2f, 3f } };
        public IEnumerable<object> VectorArgs => new object[] { new Vector3(1f, 2f, 3f) };

        public Benchmarks()
        {
            sharpNoiseModule = new SharpNoise.Modules.Terrace()
            {
                Source0 = new Constant() { ConstantValue = 5d },
                ControlPoints = new[] { 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 9d, 10d },
                InvertTerraces = true
            };

            obsidianModule = new Obsidian.WorldData.Generators.Modules.CompiledTerrace()
            {
                Source = new Constant<float>(5f),
                ControlPoints = new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f },
                Inverted = true
            };
            obsidianFunc = obsidianModule.Compile<Vector3>();
        }

        [Benchmark, ArgumentsSource(nameof(VectorArgs))]
        public float ObsidianNoiseBenchmark(Vector3 vector)
        {
            return obsidianFunc(vector);
        }

        [Benchmark, ArgumentsSource(nameof(DoubleArgs))]
        public double SharpNoiseBenchmark(double x, double y, double z)
        {
            return sharpNoiseModule.GetValue(x, y, z);
        }
    }
}
