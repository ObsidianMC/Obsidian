using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Guards the eight-point noise samplers, which only run on machines with AVX2: every lane must equal the single-point
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

    [Fact]
    public void ColumnSamplingMatchesSinglePoints()
    {
        if (!PerlinNoise.CanSampleEight)
            return;

        PerlinNoise[] noises =
        [
            new PerlinNoise(new XoroshiroRandomSource(1L)),
            new SmearedPerlinNoise(new XoroshiroRandomSource(2L), 0.5),
            new SmearedPerlinNoise(new XoroshiroRandomSource(3L), 8.0 * 684.412),
        ];

        var random = new Random(54321);
        var mismatches = new List<string>();
        foreach (var noise in noises)
        {
            for (var batch = 0; batch < Batches; batch++)
                CompareColumn(noise, random, mismatches);

            // Octave-scaled columns cover adjacent cells, skipped cells and permutation/wrap boundaries.
            // A half-cell step or larger guarantees at least three cells across the eight points.
            double[] scales = [0.25, 0.5, 0.75, 1.0, 2.0, 8.0, 64.0, 256.0];
            double[] starts = [-33554432.25, -16777216.5, -256.0, -0.5, 0.0, 255.75, 16777215.5, 33554432.0];
            foreach (var scale in scales)
            {
                foreach (var start in starts)
                {
                    foreach (var direction in new[] { -1, 1 })
                    {
                        var ys = new double[8];
                        for (var lane = 0; lane < ys.Length; lane++)
                            ys[lane] = start + direction * lane * scale;

                        CompareColumn(noise, -257.25, 255.5, ys, mismatches);
                    }
                }
            }

            // Selected columns may revisit cells or have an interior point outside both endpoints.
            double[][] selectedColumns =
            [
                [0.0, 0.5, 1.5, 1.0, 0.25, 2.0, 1.75, 2.5],
                [0.0, 0.5, 1.0, 0.25, 0.75, 2.0, 1.5, 0.0],
            ];
            foreach (var ys in selectedColumns)
                CompareColumn(noise, 255.75, -256.5, ys, mismatches);
        }

        var firstMismatches = mismatches.GetRange(0, Math.Min(10, mismatches.Count));
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} lanes differ:{Environment.NewLine}"
            + string.Join(Environment.NewLine, firstMismatches));
    }

    // Eight y of one column: half the batches step through a single lattice cell, which shares its corners between the
    // lanes, and half spread over many cells.
    private static void CompareColumn(PerlinNoise noise, Random random, List<string> mismatches)
    {
        var x = Coordinate(random);
        var z = Coordinate(random);
        double[] y;

        if (random.Next(2) == 0)
        {
            var start = Math.Floor(Coordinate(random)) + random.NextDouble() * 0.5;
            var step = random.NextDouble() * 0.06;
            y = new double[8];

            for (var i = 0; i < y.Length; i++)
                y[i] = start + i * step;
        }
        else
        {
            y = Coordinates(random);
        }

        CompareColumn(noise, x, z, y, mismatches);
    }

    private static void CompareColumn(
        PerlinNoise noise,
        double x,
        double z,
        ReadOnlySpan<double> y,
        List<string> mismatches)
    {
        var sampled = noise.GetColumn8(
            x,
            z,
            Vector256.Create(y[0], y[1], y[2], y[3]),
            Vector256.Create(y[4], y[5], y[6], y[7]));

        for (var lane = 0; lane < 8; lane++)
        {
            var expected = noise.Get(x, y[lane], z);
            var actual = sampled.GetElement(lane);
            if (BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(actual))
                mismatches.Add($"{noise.GetType().Name} at ({x}, {y[lane]}, {z}): {expected} vs {actual}");
        }
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
