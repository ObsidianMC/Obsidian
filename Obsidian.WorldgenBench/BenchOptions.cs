using Obsidian.WorldData.Generators.Mojang;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldgenBench;

internal sealed record BenchOptions(IReadOnlyList<BenchDimension> Dimensions, int Radius, long Seed, bool Cold)
{
    public const int DefaultRadius = 16;
    public const long DefaultSeed = 12345L;

    /// <summary>
    /// Whether the run generates the chunks whose hashes are recorded.
    /// </summary>
    public bool HasExpectedHashes => this.Radius == DefaultRadius && this.Seed == DefaultSeed;

    public static BenchOptions Parse(ReadOnlySpan<string> args)
    {
        var dimensions = BenchDimension.All;
        var radius = DefaultRadius;
        var seed = DefaultSeed;
        var cold = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dimension":
                    var name = args[++i];
                    dimensions = name == "all"
                        ? BenchDimension.All
                        : [BenchDimension.All.FirstOrDefault(dimension => dimension.Name == name)
                            ?? throw new ArgumentException($"Unknown dimension '{name}'.")];
                    break;
                case "--radius":
                    radius = int.Parse(args[++i]);
                    break;
                case "--seed":
                    seed = long.Parse(args[++i]);
                    break;
                case "--cold":
                    cold = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return new BenchOptions(dimensions, radius, seed, cold);
    }
}

/// <summary>
/// A dimension, the chunk its square is centred on, and the hashes of the square's chunks for the default seed and radius.
/// </summary>
internal sealed record BenchDimension(string Name, MojangDimension Dimension, int CenterX, int CenterZ, string ExpectedHash,
    string ExpectedNbtHash)
{
    // Recorded at the start of the overworld performance work (origin/1.21.x e2c0e005, see
    // docs/worldgen-overworld-performance-plan.md). Optimizations must not change them.
    public static IReadOnlyList<BenchDimension> All { get; } =
    [
        new("overworld", MojangDimension.Overworld, 6, -2, "446d8fced8e36799", "02a515930da0fd64"),
        new("nether", MojangDimension.Nether, 0, 0, "af7e0c06757df39c", "0182f3c2505ffb8c"),
        new("end", MojangDimension.End, 6, 0, "e2618139ae48a1ca", "7c2bb04c8b8081bd"),
    ];
}
