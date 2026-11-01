using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Plugins;
using System.Text.Json;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// A dimension a level stem defines: its dimension type and what generates it. Plugins' and data packs' stems are
/// read into these (see <see cref="Registries.Codecs.LevelStemData"/>), and each level of one gets its generator from
/// <see cref="CreateGenerator"/>.
/// </summary>
/// <remarks>
/// The kinds are vanilla's chunk generators: <c>minecraft:noise</c>
/// (<see cref="Biomes.WorldgenDimension"/>), <c>minecraft:flat</c> (<see cref="FlatStem"/>) and
/// <c>minecraft:debug</c> (<see cref="DebugStem"/>), and Obsidian's plugin generators (see
/// <see cref="PluginGeneratorStem"/>).
/// </remarks>
internal abstract class LevelStem
{
    private readonly string dimensionType;

    /// <param name="identifier">The level stem's identifier, which is also its level's name.</param>
    /// <param name="dimensionType">A registered dimension type.</param>
    /// <param name="plugins">The plugins whose types the generator is made of.</param>
    protected LevelStem(string identifier, string dimensionType, ImmutableArray<PluginContainer> plugins = default)
    {
        this.Identifier = identifier;
        this.dimensionType = dimensionType;
        this.Plugins = plugins.IsDefault ? [] : plugins;
    }

    /// <summary>
    /// The dimension's identifier (its level stem's), which is also its level's name.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The dimension type's codec, which decides the build range and sky light.
    /// </summary>
    public DimensionCodec DimensionType => field ??= CodecRegistry.TryGetDimension(this.dimensionType, out var codec)
        ? codec!
        : throw new InvalidOperationException($"Unknown dimension type '{this.dimensionType}'.");

    /// <summary>
    /// The plugins whose types the generator is made of. Generating calls into them, so the dimension can't generate
    /// once one unloads (see <see cref="Release"/>).
    /// </summary>
    public ImmutableArray<PluginContainer> Plugins { get; }

    /// <summary>
    /// The biomes the stem names for its generator to generate, which a reload changing makes a change to the stem.
    /// </summary>
    public abstract IReadOnlyList<BiomeCodec> SourceBiomes { get; }

    public virtual IReadOnlyList<BiomeCodec> PossibleBiomes =>
        PluginBiomeRegions.PossibleBiomes(this.Identifier, this.SourceBiomes);

    /// <summary>
    /// Whether <see cref="Release"/> dropped the generator's parts.
    /// </summary>
    public virtual bool IsReleased => false;

    /// <summary>
    /// Whether the stem left <see cref="Registries.PluginDimensions.Added"/>, as no file defines it any more or a
    /// reload replaced it, so that a level can't take it from an earlier snapshot (see
    /// <see cref="Registries.PluginDimensions.Take"/>). Unlike <see cref="IsReleased"/>, it's set for every kind of
    /// stem, including those holding no plugin's code. Set by <see cref="Registries.PluginDimensions"/>.
    /// </summary>
    public bool IsRemoved { get; set; }

    /// <summary>
    /// Drops what the generator is made of of <see cref="Plugins"/>, so that they can unload: the dimension can't
    /// generate any more. Stems made of none have nothing to drop.
    /// </summary>
    public virtual void Release()
    {
    }

    /// <summary>
    /// Creates the generator of a level of this dimension. Its <see cref="ILevelGenerator.Init"/> takes the dimension
    /// for the level (see <see cref="Registries.PluginDimensions.Take"/>).
    /// </summary>
    public abstract IStemGenerator CreateGenerator();
}

/// <summary>
/// The generator of a level of a <see cref="LevelStem"/>.
/// </summary>
internal interface IStemGenerator : ILevelGenerator
{
    /// <summary>
    /// The dimension <see cref="ILevelGenerator.Init"/> took for the level, whose dimension type the level must use,
    /// or null when it couldn't: it went since the generator was created, and the level has to be left out.
    /// </summary>
    LevelStem? Taken { get; }
}

/// <summary>
/// A level stem whose <c>generator</c> names a plugin's level generator (see
/// <see cref="Registries.PluginGenerators"/>), which is Obsidian's extension of vanilla's stem:
/// <c>{"type": "&lt;generator id&gt;", ...settings}</c>.
/// </summary>
/// <remarks>
/// The stem holds no plugin's types, so it's never released: its level generates with the plugin's generator while
/// the plugin is loaded, and stops and resumes with it (see <see cref="PluginLevelGenerator"/>), unless the stem
/// changed meanwhile: then it stays stopped until a restart.
/// </remarks>
/// <param name="identifier">The level stem's identifier.</param>
/// <param name="dimensionType">A registered dimension type.</param>
/// <param name="generator">The plugin generator's identifier.</param>
/// <param name="settings">The stem's <c>generator</c> object, given to generators that are
/// <see cref="IConfigurableLevelGenerator"/>.</param>
internal sealed class PluginGeneratorStem(
    string identifier,
    string dimensionType,
    string generator,
    JsonElement settings)
    : LevelStem(identifier, dimensionType)
{
    /// <summary>
    /// The plugin generator's identifier.
    /// </summary>
    public string Generator { get; } = generator;

    /// <summary>
    /// The stem's <c>generator</c> object, its <c>type</c> included.
    /// </summary>
    public JsonElement Settings { get; } = settings;

    public override IReadOnlyList<BiomeCodec> SourceBiomes => [];

    public override IStemGenerator CreateGenerator() => new PluginLevelGenerator(this.Generator, this);
}
