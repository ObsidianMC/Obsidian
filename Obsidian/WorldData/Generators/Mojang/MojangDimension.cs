using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.World.Generator.Noise;
using Obsidian.Plugins;
using Obsidian.Registries;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// What a level stem generates with vanilla's noise generator: its dimension type, noise settings and biome source,
/// whose biomes give the carvers. Vanilla's world preset's dimensions are the statics; plugins' are read from their
/// level stem files (see <see cref="Registries.Codecs.LevelStemData"/>).
/// </summary>
internal sealed class MojangDimension
{
    /// <summary>
    /// The overworld (dimension type <c>minecraft:overworld</c>, noise settings <c>minecraft:overworld</c>).
    /// </summary>
    public static MojangDimension Overworld { get; } = VanillaDimension(
        "minecraft:overworld",
        "minecraft:overworld",
        "minecraft:overworld",
        MultiNoiseBiomeSource.Overworld,
        () => MultiNoiseBiomeSource.OverworldBiomes);

    /// <summary>
    /// The nether (dimension type <c>minecraft:the_nether</c>, noise settings <c>minecraft:nether</c>).
    /// </summary>
    public static MojangDimension Nether { get; } = VanillaDimension(
        "minecraft:the_nether",
        "minecraft:the_nether",
        "minecraft:nether",
        MultiNoiseBiomeSource.Nether,
        () => MultiNoiseBiomeSource.NetherBiomes);

    /// <summary>
    /// The end (dimension type <c>minecraft:the_end</c>, noise settings <c>minecraft:end</c>).
    /// </summary>
    public static MojangDimension End { get; } = VanillaDimension(
        "minecraft:the_end",
        "minecraft:the_end",
        "minecraft:end",
        randomState => new TheEndBiomeSource(randomState),
        () => TheEndBiomeSource.Biomes);

    /// <summary>
    /// Every dimension vanilla's world preset generates.
    /// </summary>
    public static IReadOnlyList<MojangDimension> Vanilla { get; } = [Overworld, Nether, End];

    private readonly string dimensionType;

    // Dropped by Release, with the plugin objects they hold.
    private NoiseSetting? settings;
    private Func<RandomState, IClimateBiomeSource>? createBiomeSource;
    private Func<IReadOnlyList<BiomeCodec>> possibleBiomes;

    /// <summary>
    /// The dimension's identifier (its level stem's), which is also its level's name.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The biomes the dimension's biome source can return, in vanilla's possibleBiomes order, followed by those its
    /// plugins' biome regions add (see <see cref="PluginBiomeRegions"/>). Its generator orders the features of these
    /// biomes, and only these, together.
    /// </summary>
    public IReadOnlyList<BiomeCodec> PossibleBiomes =>
        PluginBiomeRegions.PossibleBiomes(this.Identifier, this.possibleBiomes());

    /// <summary>
    /// The possible biomes of the dimension's own biome source, without those of plugins' biome regions.
    /// </summary>
    public IReadOnlyList<BiomeCodec> SourceBiomes => this.possibleBiomes();

    /// <summary>
    /// The dimension type's codec, which decides the build range and sky light.
    /// </summary>
    public DimensionCodec DimensionType => field ??= CodecRegistry.TryGetDimension(this.dimensionType, out var codec)
        ? codec!
        : throw new InvalidOperationException($"Unknown dimension type '{this.dimensionType}'.");

    /// <summary>
    /// The noise settings' identifier, vanilla's or a plugin's, or null for ones defined inline in the level stem.
    /// </summary>
    public string? NoiseSettings { get; }

    /// <summary>
    /// The noise settings the dimension generates with.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    public NoiseSetting Settings => this.settings ?? throw this.Released();

    /// <summary>
    /// The noise settings' sea level, kept once the dimension is released.
    /// </summary>
    public int SeaLevel { get; }

    /// <summary>
    /// The dimension type's lowest block Y.
    /// </summary>
    public int MinY => this.DimensionType.Element.MinY;

    /// <summary>
    /// The dimension type's build height in blocks.
    /// </summary>
    public int Height => this.DimensionType.Element.Height;

    /// <summary>
    /// Whether the dimension type has sky light (the nether doesn't).
    /// </summary>
    public bool HasSkyLight => this.DimensionType.Element.HasSkylight;

    /// <summary>
    /// Creates the dimension's biome source for a world.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dimension was released.</exception>
    public Func<RandomState, IClimateBiomeSource> CreateBiomeSource => this.createBiomeSource ?? throw this.Released();

    /// <summary>
    /// The plugins whose types the noise settings or the biome source are made of. Generating calls into them, so the
    /// dimension can't generate once one unloads (see <see cref="Release"/>).
    /// </summary>
    public ImmutableArray<PluginContainer> Plugins { get; }

    /// <summary>
    /// The noises files define that the dimension reads by identifier, like the surface builder's
    /// <c>minecraft:surface</c> (see <see cref="RandomState.GetOrCreateNoise(string)"/>); vanilla's are found
    /// otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, BaseNoise> Noises { get; }

    /// <summary>
    /// Whether <see cref="Release"/> dropped the generator's parts.
    /// </summary>
    public bool IsReleased => this.settings is null;

    /// <param name="identifier">The level stem's identifier.</param>
    /// <param name="dimensionType">A registered dimension type.</param>
    /// <param name="noiseSettings">The noise settings' identifier, or null for ones defined inline.</param>
    /// <param name="settings">The noise settings.</param>
    /// <param name="createBiomeSource">Creates the biome source for a world.</param>
    /// <param name="possibleBiomes">The biome source's possible biomes, without creating one.</param>
    /// <param name="plugins">The plugins whose types the noise settings or the biome source are made of.</param>
    /// <param name="noises">The noises files define, read by identifier before vanilla's; none by default.</param>
    public MojangDimension(
        string identifier,
        string dimensionType,
        string? noiseSettings,
        NoiseSetting settings,
        Func<RandomState, IClimateBiomeSource> createBiomeSource,
        Func<IReadOnlyList<BiomeCodec>> possibleBiomes,
        ImmutableArray<PluginContainer> plugins = default,
        IReadOnlyDictionary<string, BaseNoise>? noises = null)
    {
        this.Identifier = identifier;
        this.dimensionType = dimensionType;
        this.NoiseSettings = noiseSettings;
        this.settings = settings;
        this.SeaLevel = settings.SeaLevel;
        // Plugins' biome regions join the dimension's biome source as it's created.
        this.createBiomeSource = PluginBiomeRegions.Wrap(identifier, createBiomeSource);
        this.possibleBiomes = possibleBiomes;
        this.Plugins = plugins.IsDefault ? [] : plugins;
        this.Noises = noises ?? FrozenDictionary<string, BaseNoise>.Empty;
    }

    /// <summary>
    /// Drops the noise settings and the biome source, so that the plugins they're made of can unload: the dimension
    /// can't generate any more. Its possible biomes stay, as features are still ordered with them.
    /// </summary>
    public void Release()
    {
        IReadOnlyList<BiomeCodec> biomes = [.. this.possibleBiomes()];
        this.possibleBiomes = () => biomes;
        this.createBiomeSource = null;
        this.settings = null;
    }

    private static MojangDimension VanillaDimension(
        string identifier,
        string dimensionType,
        string noiseSettings,
        Func<RandomState, IClimateBiomeSource> createBiomeSource,
        Func<IReadOnlyList<BiomeCodec>> possibleBiomes) => new(
        identifier,
        dimensionType,
        noiseSettings,
        NoiseRegistry.NoiseSettings.All[noiseSettings],
        createBiomeSource,
        possibleBiomes);

    private InvalidOperationException Released() =>
        new($"{this.Identifier} was released when a plugin it generates with unloaded.");
}
