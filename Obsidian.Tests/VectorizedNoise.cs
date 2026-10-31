using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Guards the eight-point noise sampler, which only runs on machines with AVX2: every lane must equal the single-point
/// sampler bit for bit, or worldgen output would depend on the CPU.
/// </summary>
public class VectorizedNoise
{
    private const int Batches = 20_000;

    [Fact]
    public void EightPointSamplingMatchesSinglePoints()
    {
        if (!PerlinNoise.CanSampleEight)
            return;

        PerlinNoise[] noises =
        [
            new PerlinNoise(new XoroshiroRandomSource(1L)),
            new SmearedPerlinNoise(new XoroshiroRandomSource(2L), 0.5),
            new SmearedPerlinNoise(new XoroshiroRandomSource(3L), 8.0 * 684.412),
        ];

        var random = new Random(12345);
        var mismatches = new List<string>();
        foreach (var noise in noises)
        {
            for (var batch = 0; batch < Batches; batch++)
                CompareBatch(noise, random, mismatches);
        }

        var firstMismatches = mismatches.GetRange(0, Math.Min(10, mismatches.Count));
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} lanes differ:{Environment.NewLine}"
            + string.Join(Environment.NewLine, firstMismatches));
    }

    private static void CompareBatch(PerlinNoise noise, Random random, List<string> mismatches)
    {
        var x = Coordinates(random);
        var y = Coordinates(random);
        var z = Coordinates(random);

        var sampled = noise.Get8(
            Vector256.Create(x[0], x[1], x[2], x[3]),
            Vector256.Create(x[4], x[5], x[6], x[7]),
            Vector256.Create(y[0], y[1], y[2], y[3]),
            Vector256.Create(y[4], y[5], y[6], y[7]),
            Vector256.Create(z[0], z[1], z[2], z[3]),
            Vector256.Create(z[4], z[5], z[6], z[7]));

        for (var lane = 0; lane < 8; lane++)
        {
            var expected = noise.Get(x[lane], y[lane], z[lane]);
            var actual = sampled.GetElement(lane);
            if (BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(actual))
                mismatches.Add($"{noise.GetType().Name} at ({x[lane]}, {y[lane]}, {z[lane]}): {expected} vs {actual}");
        }
    }

    // A mix of the coordinates density functions produce: small and huge values, whole numbers, values next to the
    // wrap period's edges, and negative zero.
    private static double[] Coordinates(Random random)
    {
        var values = new double[8];
        for (var i = 0; i < values.Length; i++)
            values[i] = Coordinate(random);

        return values;
    }

    private static double Coordinate(Random random)
    {
        return random.Next(6) switch
        {
            0 => (random.NextDouble() - 0.5) * 4.0,
            1 => (random.NextDouble() - 0.5) * 2.0e8,
            2 => Math.Round((random.NextDouble() - 0.5) * 2.0e4),
            3 => random.NextDouble() < 0.5 ? 16777216.0 : -16777216.0,
            4 => Math.BitDecrement(33554432.0) * (random.NextDouble() < 0.5 ? 1 : -1),
            _ => random.NextDouble() < 0.5 ? -0.0 : random.NextDouble() * 512.0,
        };
    }
}
