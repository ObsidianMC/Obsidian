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
/// A dimension, the chunk its square is centred on, and the hashes of the square's chunks for the default seed and radius:
/// the live generator's, and the 26.3 pipeline's (<see cref="CurrentPreset"/> is its biome preset).
/// </summary>
internal sealed record BenchDimension(string Name, MojangDimension Dimension, int CenterX, int CenterZ, string ExpectedHash,
    string ExpectedNbtHash, string CurrentPreset, string CurrentHash, string CurrentNbtHash)
{
    // Recorded at the start of the overworld performance work (origin/1.21.x e2c0e005), and again after the mod system
    // (#553) changed one overworld block and the saved chunk format (see docs/worldgen-overworld-performance-plan.md).
    // Optimizations must not change them. The 26.3 pipeline's were recorded when its performance work started
    // (origin/26.3.x 80555196).
    public static IReadOnlyList<BenchDimension> All { get; } =
    [
        new("overworld", MojangDimension.Overworld, 6, -2, "c73225baaf86c6fe", "0525f8628623dc64",
            "minecraft:overworld", "c41960463b5b0d88", "c2f3c20883d6d150"),
        new("nether", MojangDimension.Nether, 0, 0, "af7e0c06757df39c", "badbc176a6e2a3fd",
            "minecraft:nether", "4771e394196351ed", "0796eaa405cabb9b"),
        new("end", MojangDimension.End, 6, 0, "e2618139ae48a1ca", "9fbd717769bfb03a",
            "minecraft:the_end", "d28d968cb752b41a", "b75171a96e6db01e"),
    ];
}
