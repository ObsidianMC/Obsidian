using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang.Regions;

/// <summary>
/// Where a dimension's biome regions are: each column belongs to one region, or to the dimension's own layout. Both
/// the 1.21.11 biome source (<see cref="RegionBiomeSource"/>) and the 26.3 one (<see cref="Biomes.ClimateSource"/>)
/// place regions with it, so a world's regions are in the same places in both.
/// </summary>
/// <remarks>
/// Regions are spread over a grid of cells <see cref="CellSize"/> quarts wide. Each cell has a point at a random
/// place in its middle half, and every column belongs to the nearest point's cell, so regions are irregular polygons
/// about 1024 blocks across. A cell picks a region at random by weight, the dimension's own layout weighing
/// <see cref="DimensionWeight"/>. The randoms are hashes of the cell's coordinates and a seed from the world seed and
/// the dimension, so a world generates the same regions every time, and draws nothing from vanilla's randoms.
/// </remarks>
internal sealed class RegionLayout
{
    /// <summary>
    /// The weight of the dimension's own layout against its regions' weights. With one region of weight 10, half of
    /// the cells are the region's.
    /// </summary>
    public const int DimensionWeight = 10;

    /// <summary>
    /// The width of a cell in quarts (1024 blocks).
    /// </summary>
    public const int CellSize = 1 << CellShift;

    private const int CellShift = 8;

    // Cell points are in the middle half of their cell, so the nearest one is always in the 3x3 cells around.
    private const int PointRange = CellSize / 2;

    private readonly ulong seed;

    // Each cell picks the first region whose cumulative weight is above its random, below their total; the
    // dimension's own layout is null.
    private readonly BiomeRegion?[] regions;
    private readonly int[] cumulativeWeights;

    /// <param name="identifier">The dimension's identifier, which seeds where its regions are.</param>
    /// <param name="regions">The dimension's regions, in a fixed order (by identifier), which decides where each is;
    /// mustn't be empty.</param>
    /// <param name="worldRandom">The world's positional random, from the world seed and the noise settings'
    /// <c>legacy_random_source</c>.</param>
    public RegionLayout(string identifier, ImmutableArray<BiomeRegion> regions, IPositionalRandomFactory worldRandom)
    {
        this.seed = (ulong)worldRandom.FromHashOf($"obsidian:biome_regions/{identifier}").NextLong();
        this.regions = [null, .. regions];
        this.cumulativeWeights = new int[this.regions.Length];
        this.Regions = regions;

        var total = 0;
        for (var i = 0; i < this.regions.Length; i++)
        {
            total += this.regions[i]?.Weight ?? DimensionWeight;
            this.cumulativeWeights[i] = total;
        }
    }

    /// <summary>
    /// The dimension's regions, in their order.
    /// </summary>
    public ImmutableArray<BiomeRegion> Regions { get; }

    /// <summary>
    /// The region a column is in, or null for the dimension's own layout.
    /// </summary>
    public BiomeRegion? RegionAt(int quartX, int quartZ)
    {
        var cellX = quartX >> CellShift;
        var cellZ = quartZ >> CellShift;
        var nearest = long.MaxValue;
        var nearestHash = 0UL;

        for (var x = cellX - 1; x <= cellX + 1; x++)
        {
            for (var z = cellZ - 1; z <= cellZ + 1; z++)
            {
                var hash = this.Hash(x, z);
                long pointX = (x << CellShift) + PointRange / 2 + (int)(hash % PointRange);
                long pointZ = (z << CellShift) + PointRange / 2 + (int)((hash >> 16) % PointRange);
                var distance = (quartX - pointX) * (quartX - pointX) + (quartZ - pointZ) * (quartZ - pointZ);

                if (distance < nearest)
                {
                    nearest = distance;
                    nearestHash = hash;
                }
            }
        }

        var pick = (int)((nearestHash >> 32) % (ulong)this.cumulativeWeights[^1]);
        var index = 0;

        while (this.cumulativeWeights[index] <= pick)
            index++;

        return this.regions[index];
    }

    /// <summary>
    /// The dimension's possible biomes followed by those its regions add, in the regions' order. Without regions,
    /// they're the dimension's, so features are ordered exactly as without regions.
    /// </summary>
    public static IReadOnlyList<BiomeCodec> PossibleBiomes(
        IReadOnlyList<BiomeCodec> dimensionBiomes,
        ImmutableArray<BiomeRegion> regions)
    {
        if (regions.IsDefaultOrEmpty)
            return dimensionBiomes;

        var names = dimensionBiomes.Select(biome => biome.Name).ToHashSet();
        var biomes = dimensionBiomes.ToList();

        foreach (var region in regions)
        {
            foreach (var biome in region.Biomes(dimensionBiomes))
            {
                if (names.Add(biome.Name))
                    biomes.Add(biome);
            }
        }

        return biomes;
    }

    // A cell's random: SplitMix64's mix of the seed and the cell's coordinates.
    private ulong Hash(int cellX, int cellZ) =>
        Mix(this.seed ^ Mix(((ulong)(uint)cellX << 32) | (uint)cellZ));

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;

        return value ^ (value >> 31);
    }
}
