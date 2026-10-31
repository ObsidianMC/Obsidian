using Obsidian.WorldData.Generators.Mojang;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldgenBench;

internal sealed record BenchOptions(IReadOnlyList<BenchDimension> Dimensions, int Radius, long Seed, bool Cold,
    BenchGeneratorKind Generator)
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
        var generator = BenchGeneratorKind.Legacy;

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
                case "--generator":
                    generator = args[++i] switch
                    {
                        "1.21.11" => BenchGeneratorKind.Legacy,
                        "26.3" => BenchGeneratorKind.Current,
                        var unknown => throw new ArgumentException($"Unknown generator '{unknown}'.")
                    };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }

        return new BenchOptions(dimensions, radius, seed, cold, generator);
    }
}

/// <summary>
/// A dimension, the chunk its square is centred on, and the hashes of the square's chunks for the default seed and
/// radius: the live generator's, and the 26.3 pipeline's (<see cref="CurrentPreset"/> is its biome preset).
/// </summary>
internal sealed record BenchDimension(string Name, MojangDimension Dimension, int CenterX, int CenterZ, string ExpectedHash,
    string ExpectedNbtHash, string CurrentPreset, string CurrentHash, string CurrentNbtHash)
{
    // Optimizations must not change them. The live generator's were recorded on origin/26.3.x 80555196, whose block
    // states and saved chunks differ from 1.21.x's (on 1.21.x they were c73225baaf86c6fe and 0525f8628623dc64 for the
    // overworld). The 26.3 pipeline's were re-recorded after #638 (on dc0416fa), which keeps stored heightmaps up to
    // date on every block write; the bench doesn't yet hand decoration's final heightmaps to the saved chunk.
    public static IReadOnlyList<BenchDimension> All { get; } =
    [
        new("overworld", MojangDimension.Overworld, 6, -2, "6f824c2e873bca90", "869a5df6644e5d41",
            "minecraft:overworld", "5ec7961dd197dba8", "ea3dd2b92140e97a"),
        new("nether", MojangDimension.Nether, 0, 0, "2880fa86ca3c5e6c", "fec88678179d32d9",
            "minecraft:nether", "161cdadb4851906d", "e32931894dfc95c2"),
        new("end", MojangDimension.End, 6, 0, "5c34c3f5867f27ac", "d940fd5e665d6164",
            "minecraft:the_end", "d0bd6e11ce09aa81", "d4ee92f29c956268"),
    ];
}
