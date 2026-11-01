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
/// A dimension (its level stem), the chunk its square is centred on, and the hashes of the square's chunks for the
/// default seed and radius.
/// </summary>
internal sealed record BenchDimension(string Name, string Identifier, int CenterX, int CenterZ, string ExpectedHash,
    string ExpectedNbtHash)
{
    // Optimizations must not change them. They include BiomeTerrain.CompleteHeightmaps: decoration's final maps are
    // handed to chunk storage after all neighboring decoration, before lighting and saving. Its surfaces read the
    // biomes stored in the chunk and its neighbors, like vanilla's, rather than fresh climate searches, which resolve
    // some exact ties differently. Fluid/shape post-processing runs before heightmaps and lighting. Completion retains
    // dark Nether mushrooms (while checking nearby emission), and ticks marked water into bubble columns with block
    // checks; FULL completion captures back these final blocks and ticks.
    public static IReadOnlyList<BenchDimension> All { get; } =
    [
        new("overworld", "minecraft:overworld", 6, -2, "2d54ff3885c4051e", "549e4efa115c90f3"),
        new("nether", "minecraft:the_nether", 0, 0, "3ef4470d0a7794b8", "d7a5b14aeb9f4477"),
        new("end", "minecraft:the_end", 6, 0, "5c34c3f5867f27ac", "762ccf03eda84ce2"),
    ];
}
