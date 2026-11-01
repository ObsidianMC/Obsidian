using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang.Regions;

/// <summary>
/// A dimension's biome source with plugins' biome regions: each position belongs to one region, the dimension's own
/// layout or one of the regions, and gets that region's biome. Terrain doesn't depend on biomes, so only biomes, and
/// what follows them (surface, features, carvers, spawns and structures), change.
/// </summary>
/// <remarks>
/// Where each region is comes from <see cref="RegionLayout"/>, which the 26.3 pipeline shares.
/// </remarks>
internal sealed class RegionBiomeSource : IClimateBiomeSource
{
    /// <inheritdoc cref="RegionLayout.DimensionWeight"/>
    public const int DimensionWeight = RegionLayout.DimensionWeight;

    /// <inheritdoc cref="RegionLayout.CellSize"/>
    public const int CellSize = RegionLayout.CellSize;

    private readonly IClimateBiomeSource dimension;
    private readonly RandomState randomState;
    private readonly RegionLayout layout;

    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; }

    private RegionBiomeSource(
        string identifier,
        IClimateBiomeSource dimension,
        ImmutableArray<BiomeRegion> regions,
        RandomState randomState)
    {
        this.dimension = dimension;
        this.randomState = randomState;
        this.layout = new RegionLayout(identifier, regions, randomState.Random);
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

    /// <inheritdoc cref="RegionLayout.PossibleBiomes"/>
    public static IReadOnlyList<BiomeCodec> GetPossibleBiomes(
        IReadOnlyList<BiomeCodec> dimensionBiomes,
        ImmutableArray<BiomeRegion> regions) => RegionLayout.PossibleBiomes(dimensionBiomes, regions);

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(this.randomState.ClimateSampler, quartX, quartY, quartZ);

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ) =>
        this.RegionAt(quartX, quartZ) is BiomeRegion region
            ? region.GetNoiseBiome(this.dimension, sampler, quartX, quartY, quartZ)
            : this.dimension.GetNoiseBiome(sampler, quartX, quartY, quartZ);

    /// <summary>
    /// The region a column is in, or null for the dimension's own layout.
    /// </summary>
    internal BiomeRegion? RegionAt(int quartX, int quartZ) => this.layout.RegionAt(quartX, quartZ);
}
