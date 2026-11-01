using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// How a dimension's biome source picks biomes in the 26.3 pipeline, as a level stem's <c>biome_source</c> names it:
/// vanilla's <c>multi_noise</c>, <c>the_end</c>, <c>fixed</c> and <c>checkerboard</c>, or a plugin's type.
/// <see cref="ClimateSource"/> samples the climate it reads and stores the biomes it picks.
/// </summary>
internal abstract class BiomeLayout
{
    /// <summary>
    /// The biomes the layout can pick, in vanilla's possibleBiomes order, which orders the dimension's features.
    /// </summary>
    public abstract IReadOnlyList<string> PossibleBiomes { get; }

    /// <summary>
    /// The biome at quart coordinates.
    /// </summary>
    /// <param name="climate">The dimension's climate, which the layout may sample.</param>
    public abstract BiomeCodec GetBiome(ClimateSource climate, int quartX, int quartY, int quartZ);

    /// <summary>
    /// The layout of one of vanilla's biome presets: <c>minecraft:overworld</c> or <c>minecraft:nether</c>, the
    /// multi-noise parameter lists, or <c>minecraft:the_end</c>.
    /// </summary>
    public static BiomeLayout Vanilla(WorldgenData data, string preset) => preset == "minecraft:the_end"
        ? EndLayout.Instance
        : new MultiNoiseLayout(BiomeParameters.Read(data, preset));

    /// <summary>
    /// Every biome a layout can pick, as their registered codecs.
    /// </summary>
    /// <exception cref="InvalidOperationException">A biome isn't registered.</exception>
    public IReadOnlyList<BiomeCodec> PossibleCodecs() => [.. this.PossibleBiomes.Select(Codec)];

    /// <summary>
    /// A registered biome by name.
    /// </summary>
    /// <exception cref="InvalidOperationException">It isn't registered.</exception>
    public static BiomeCodec Codec(string name) => CodecRegistry.TryGetBiome(name, out var biome)
        ? biome!
        : throw new InvalidOperationException($"Unknown biome '{name}'.");
}

/// <summary>
/// Vanilla's <c>multi_noise</c>: the biome whose parameters are nearest the climate. <see cref="ClimateSource"/>
/// searches its tree itself, so that each chunk's searches can start from the one before.
/// </summary>
internal sealed class MultiNoiseLayout(BiomeParameters parameters) : BiomeLayout
{
    public BiomeParameters Parameters { get; } = parameters;

    public override IReadOnlyList<string> PossibleBiomes { get; } =
        [.. parameters.Entries.Select(entry => entry.Biome).Distinct()];

    public override BiomeCodec GetBiome(ClimateSource climate, int quartX, int quartY, int quartZ) =>
        Codec(this.Parameters.Tree.Search(climate.Sample(quartX, quartY, quartZ)));
}

/// <summary>
/// Vanilla's <c>the_end</c>: the main island, and the outer islands' biomes by erosion.
/// </summary>
internal sealed class EndLayout : BiomeLayout
{
    public static EndLayout Instance { get; } = new();

    private EndLayout()
    {
    }

    public override IReadOnlyList<string> PossibleBiomes { get; } =
    [
        "minecraft:the_end",
        "minecraft:end_highlands",
        "minecraft:end_midlands",
        "minecraft:small_end_islands",
        "minecraft:end_barrens"
    ];

    public override BiomeCodec GetBiome(ClimateSource climate, int quartX, int quartY, int quartZ) =>
        climate.EndBiome(quartX, quartY, quartZ);
}

/// <summary>
/// A biome source that doesn't read the climate, such as vanilla's <c>fixed</c> and <c>checkerboard</c>, which the
/// 1.21.11 pipeline's sources (<see cref="FixedBiomeSource"/>, <see cref="CheckerboardBiomeSource"/>) implement for
/// both pipelines.
/// </summary>
/// <param name="source">The source; its climate sampler is never read.</param>
internal sealed class PositionalLayout(IClimateBiomeSource source) : BiomeLayout
{
    public override IReadOnlyList<string> PossibleBiomes { get; } =
        [.. source.PossibleBiomes.Select(biome => biome.Name)];

    public override BiomeCodec GetBiome(ClimateSource climate, int quartX, int quartY, int quartZ) =>
        source.GetNoiseBiome(quartX, quartY, quartZ);
}

/// <summary>
/// A biome source type a plugin registered (<see cref="BiomeSourceBase"/>), which reads the 26.3 climate.
/// </summary>
/// <param name="source">The plugin's source.</param>
/// <param name="possibleBiomes">Its possible biomes, read once when its level stem was read.</param>
internal sealed class PluginLayout(BiomeSourceBase source, IReadOnlyList<BiomeCodec> possibleBiomes) : BiomeLayout
{
    private readonly FrozenDictionary<string, BiomeCodec> biomes =
        possibleBiomes.ToFrozenDictionary(biome => biome.Name);

    public override IReadOnlyList<string> PossibleBiomes { get; } = [.. possibleBiomes.Select(biome => biome.Name)];

    public override BiomeCodec GetBiome(ClimateSource climate, int quartX, int quartY, int quartZ)
    {
        var name = source.GetBiome(quartX, quartY, quartZ, climate);

        if (this.biomes.TryGetValue(name, out var biome))
            return biome;

        throw new InvalidOperationException($"{source.Type} gave {name}, which isn't one of its possible biomes.");
    }
}
