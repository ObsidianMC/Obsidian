using Obsidian.API.Registry.Codecs.Biomes;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Generators.Mojang.Regions;

/// <summary>
/// A plugin's alternative biome layout for a dimension, read from a <c>biome_region</c> file (see
/// <see cref="Registries.PluginBiomeRegions"/>). Where a <see cref="RegionBiomeSource"/> picks the region, its biomes
/// generate instead of the dimension's own.
/// </summary>
/// <param name="identifier">The region's identifier, its file's, which orders regions.</param>
/// <param name="dimension">The level stem identifier of the dimension the region is for.</param>
/// <param name="weight">How often the region is picked, against the other regions' weights and
/// <see cref="RegionBiomeSource.DimensionWeight"/>.</param>
internal abstract class BiomeRegion(string identifier, string dimension, int weight)
{
    public string Identifier { get; } = identifier;

    public string Dimension { get; } = dimension;

    public int Weight { get; } = weight;

    /// <summary>
    /// The biomes the region can give, given those the dimension's own biome source can.
    /// </summary>
    public abstract IEnumerable<BiomeCodec> Biomes(IReadOnlyList<BiomeCodec> dimensionBiomes);

    /// <summary>
    /// Gets the region's biome at quart coordinates.
    /// </summary>
    /// <param name="dimension">The dimension's own biome source.</param>
    /// <param name="sampler">The climate sampler to read.</param>
    public abstract BiomeCodec GetNoiseBiome(
        IClimateBiomeSource dimension,
        ClimateSampler sampler,
        int quartX,
        int quartY,
        int quartZ);
}

/// <summary>
/// A region that keeps the dimension's own layout with some of its biomes swapped for others.
/// </summary>
/// <param name="replacements">The biome given instead of each biome, by the replaced biome's identifier.</param>
internal sealed class ReplacingRegion(
    string identifier,
    string dimension,
    int weight,
    FrozenDictionary<string, BiomeCodec> replacements) : BiomeRegion(identifier, dimension, weight)
{
    public FrozenDictionary<string, BiomeCodec> Replacements { get; } = replacements;

    // A biome the dimension never gives is never replaced, so its replacement isn't possible.
    public override IEnumerable<BiomeCodec> Biomes(IReadOnlyList<BiomeCodec> dimensionBiomes) => dimensionBiomes
        .Select(biome => this.Replacements.GetValueOrDefault(biome.Name) ?? biome);

    // Swapping biomes leaves the parameter list's points as they are, so the nearest point is the dimension's own.
    public override BiomeCodec GetNoiseBiome(
        IClimateBiomeSource dimension,
        ClimateSampler sampler,
        int quartX,
        int quartY,
        int quartZ)
    {
        var biome = dimension.GetNoiseBiome(sampler, quartX, quartY, quartZ);

        return this.Replacements.GetValueOrDefault(biome.Name) ?? biome;
    }
}

/// <summary>
/// A region with a parameter list of its own, picking biomes from the climate like a <c>multi_noise</c> biome source.
/// </summary>
/// <param name="values">The list's entries in their order, which decides ties; mustn't be empty.</param>
internal sealed class ParameterListRegion(
    string identifier,
    string dimension,
    int weight,
    IReadOnlyList<(ParameterPoint Point, BiomeCodec Biome)> values) : BiomeRegion(identifier, dimension, weight)
{
    private readonly BiomeCodec[] biomes = [.. values.Select(value => value.Biome).DistinctBy(biome => biome.Name)];

    // Built when the region first generates, like a level stem's parameter list.
    private readonly Lazy<BiomeParameterTree<BiomeCodec>> tree = new(() => new BiomeParameterTree<BiomeCodec>(values));

    public override IEnumerable<BiomeCodec> Biomes(IReadOnlyList<BiomeCodec> dimensionBiomes) => this.biomes;

    public override BiomeCodec GetNoiseBiome(
        IClimateBiomeSource dimension,
        ClimateSampler sampler,
        int quartX,
        int quartY,
        int quartZ) => this.tree.Value.Search(sampler.Sample(quartX, quartY, quartZ));
}
