using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang.Regions;

/// <summary>
/// A dimension's biome source with plugins' biome regions: each position belongs to one region, the dimension's own
/// layout or one of the regions, and gets that region's biome. Terrain doesn't depend on biomes, so only biomes, and
/// what follows them (surface, features, carvers, spawns and structures), change.
/// </summary>
/// <remarks>
/// Regions are spread over a grid of cells <see cref="CellSize"/> quarts wide. Each cell has a point at a random
/// place in its middle half, and every column belongs to the nearest point's cell, so regions are irregular polygons
/// about 1024 blocks across. A cell picks a region at random by weight, the dimension's own layout weighing
/// <see cref="DimensionWeight"/>. The randoms are hashes of the cell's coordinates and a seed from the world seed and
/// the dimension, so a world generates the same regions every time, and draws nothing from vanilla's randoms.
/// </remarks>
internal sealed class RegionBiomeSource : IClimateBiomeSource
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

    private readonly IClimateBiomeSource dimension;
    private readonly RandomState randomState;
    private readonly ulong seed;

    // Each cell picks the first region whose cumulative weight is above its random, below their total; the
    // dimension's own layout is null.
    private readonly BiomeRegion?[] regions;
    private readonly int[] cumulativeWeights;

    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; }

    private RegionBiomeSource(
        string identifier,
        IClimateBiomeSource dimension,
        ImmutableArray<BiomeRegion> regions,
        RandomState randomState)
    {
        this.dimension = dimension;
        this.randomState = randomState;
        this.seed = (ulong)randomState.Random.FromHashOf($"obsidian:biome_regions/{identifier}").NextLong();
        this.regions = [null, .. regions];
        this.cumulativeWeights = new int[this.regions.Length];

        var total = 0;
        for (var i = 0; i < this.regions.Length; i++)
        {
            total += this.regions[i]?.Weight ?? DimensionWeight;
            this.cumulativeWeights[i] = total;
        }

        this.PossibleBiomes = GetPossibleBiomes(dimension.PossibleBiomes, regions);
    }

    /// <summary>
    /// The dimension's biome source with its regions, or the source itself when it has none, so that it generates
    /// exactly as without regions.
    /// </summary>
    /// <param name="identifier">The dimension's identifier, which seeds where its regions are.</param>
    /// <param name="dimension">The dimension's own biome source.</param>
    /// <param name="regions">The dimension's regions, in a fixed order (by identifier), which decides where each is.
    /// </param>
    /// <param name="randomState">The world's random state.</param>
    public static IClimateBiomeSource Create(
        string identifier,
        IClimateBiomeSource dimension,
        ImmutableArray<BiomeRegion> regions,
        RandomState randomState) => regions.IsDefaultOrEmpty
        ? dimension
        : new RegionBiomeSource(identifier, dimension, regions, randomState);

    /// <summary>
    /// The dimension's possible biomes followed by those its regions add, in the regions' order. Without regions,
    /// they're the dimension's, so features are ordered exactly as without regions.
    /// </summary>
    public static IReadOnlyList<BiomeCodec> GetPossibleBiomes(
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

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(this.randomState.ClimateSampler, quartX, quartY, quartZ);

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ) =>
        this.RegionAt(quartX, quartZ) is BiomeRegion region
            ? region.GetNoiseBiome(this.dimension, sampler, quartX, quartY, quartZ)
            : this.dimension.GetNoiseBiome(sampler, quartX, quartY, quartZ);

    /// <summary>
    /// The region a column is in, or null for the dimension's own layout.
    /// </summary>
    internal BiomeRegion? RegionAt(int quartX, int quartZ)
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
