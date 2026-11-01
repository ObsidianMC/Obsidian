using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Registries;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Generators.Mojang.Regions;

/// <summary>
/// A plugin's alternative biome layout for a dimension, read from a <c>biome_region</c> file (see
/// <see cref="Registries.PluginBiomeRegions"/>). Where <see cref="RegionLayout"/> places the region, its biomes
/// generate instead of the dimension's own.
/// </summary>
/// <param name="identifier">The region's identifier, its file's, which orders regions.</param>
/// <param name="dimension">The level stem identifier of the dimension the region is for.</param>
/// <param name="weight">How often the region is picked, against the other regions' weights and
/// <see cref="RegionLayout.DimensionWeight"/>.</param>
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
    /// Whether <see cref="Select"/> reads the climate rather than the dimension's own biome.
    /// </summary>
    public abstract bool ReadsClimate { get; }

    /// <summary>
    /// Gets the region's biome in the 26.3 pipeline (see <see cref="Biomes.ClimateSource"/>).
    /// </summary>
    /// <param name="dimensionBiome">The biome the dimension's own layout gives there, or null when the region
    /// <see cref="ReadsClimate"/>.</param>
    /// <param name="climate">The quantized climate there, when the region <see cref="ReadsClimate"/>.</param>
    public abstract BiomeCodec Select(BiomeCodec? dimensionBiome, in TargetPoint climate);

    /// <summary>
    /// The region with the biome tags it names resolved to their biomes as they are now, to generate with.
    /// </summary>
    public virtual BiomeRegion Resolve() => this;
}

/// <summary>
/// A region that keeps the dimension's own layout with some of its biomes swapped for others.
/// </summary>
/// <param name="replacements">The biome given instead of each biome, by the replaced biome's identifier.</param>
/// <param name="tagReplacements">The biome given instead of each biome of a tag, by the tag's identifier, in the
/// order they're written: a biome named itself, or by an earlier tag, keeps that replacement. Generating uses the
/// region <see cref="Resolve"/>d.</param>
internal sealed class ReplacingRegion(
    string identifier,
    string dimension,
    int weight,
    FrozenDictionary<string, BiomeCodec> replacements,
    ImmutableArray<(string Tag, BiomeCodec Biome)> tagReplacements = default)
    : BiomeRegion(identifier, dimension, weight)
{
    private readonly ImmutableArray<(string Tag, BiomeCodec Biome)> tagReplacements =
        tagReplacements.IsDefault ? [] : tagReplacements;

    /// <summary>
    /// The biome given instead of each biome named, without the biomes of tags until resolved.
    /// </summary>
    public FrozenDictionary<string, BiomeCodec> Replacements { get; } = replacements;

    // A biome the dimension never gives is never replaced, so its replacement isn't possible.
    public override IEnumerable<BiomeCodec> Biomes(IReadOnlyList<BiomeCodec> dimensionBiomes)
    {
        var replacements = ((ReplacingRegion)this.Resolve()).Replacements;

        return dimensionBiomes.Select(biome => replacements.GetValueOrDefault(biome.Name) ?? biome);
    }

    // A tag that doesn't exist (yet) replaces nothing.
    public override BiomeRegion Resolve()
    {
        if (this.tagReplacements.IsEmpty)
            return this;

        var resolved = new Dictionary<string, BiomeCodec>(this.Replacements);

        foreach (var (tag, biome) in this.tagReplacements)
        {
            if (!TagsRegistry.TryGet(PluginTags.Biome, tag, out var found))
                continue;

            foreach (var id in found.Entries)
            {
                if (CodecRegistry.TryGetBiome(id, out var replaced))
                    resolved.TryAdd(replaced!.Name, biome);
            }
        }

        return new ReplacingRegion(this.Identifier, this.Dimension, this.Weight, resolved.ToFrozenDictionary());
    }

    public override bool ReadsClimate => false;

    // Swapping biomes leaves the parameter list's points as they are, so the nearest point is the dimension's own.
    public override BiomeCodec Select(BiomeCodec? dimensionBiome, in TargetPoint climate) =>
        this.Replacements.GetValueOrDefault(dimensionBiome!.Name) ?? dimensionBiome;
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

    // Built when the region first generates, like a level stem's parameter list. 26.3's trees have 19 children a node, which decides exact ties (see BiomeParameters).
    private readonly Lazy<BiomeParameterTree<BiomeCodec>> tree26 =
        new(() => new BiomeParameterTree<BiomeCodec>(values, childrenPerNode: 19));

    public override IEnumerable<BiomeCodec> Biomes(IReadOnlyList<BiomeCodec> dimensionBiomes) => this.biomes;

    public override bool ReadsClimate => true;

    public override BiomeCodec Select(BiomeCodec? dimensionBiome, in TargetPoint climate) =>
        this.tree26.Value.Search(climate);
}
