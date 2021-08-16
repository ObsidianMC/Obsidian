using BenchmarkDotNet.Running;
using Obsidian.WorldData.Generators.Modules;
using System;
using System.Diagnostics;
using System.Numerics;

namespace Benchmark
{
    class Program
    {
        static void Main()
        {
            DebugTest();
            BenchmarkRunner.Run<Benchmarks>();
        }

        [Conditional("DEBUG")]
        static void DebugTest()
        {
            var sharpModule = new SharpNoise.Modules.Billow()
            {
                Quality = SharpNoise.NoiseQuality.Best,
                Seed = 12345678
            };

            var obsidianModule = new Obsidian.WorldData.Generators.Modules.CompiledTerrace()
            {
                Source = new Constant<float>(3f),
                ControlPoints = new[] { 1f, 2f, 3f, 4f, 5f },
                Inverted = true
            };
            Func<Vector3, float> func = obsidianModule.Compile<Vector3>();

            Console.WriteLine(obsidianModule.ToString<Vector3>());
            ;

            Console.WriteLine("SUCCESS");
            Console.ReadKey(true);
            Environment.Exit(0);
        }
    }

    public class Test : SharpNoise.Modules.Module
    {
        public Test() : base(0)
        {
        }

        public override double GetValue(double x, double y, double z)
        {
            return z;
        }
    }
}
