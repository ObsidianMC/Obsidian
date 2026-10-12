using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// What a level stem generates with vanilla's noise generator: its dimension type, noise settings, biome source and
/// carvers. Vanilla's world preset's dimensions are the statics; plugins' are read from their level stem files (see
/// <see cref="Registries.Codecs.LevelStemData"/>).
/// </summary>
internal sealed class MojangDimension
{
    /// <summary>
    /// The overworld (dimension type <c>minecraft:overworld</c>, noise settings <c>minecraft:overworld</c>).
    /// </summary>
    public static MojangDimension Overworld { get; } = new(
        "minecraft:overworld",
        "minecraft:overworld",
        "minecraft:overworld",
        MultiNoiseBiomeSource.Overworld,
        () => MultiNoiseBiomeSource.OverworldBiomes);

    /// <summary>
    /// The nether (dimension type <c>minecraft:the_nether</c>, noise settings <c>minecraft:nether</c>).
    /// </summary>
    public static MojangDimension Nether { get; } = new(
        "minecraft:the_nether",
        "minecraft:the_nether",
        "minecraft:nether",
        MultiNoiseBiomeSource.Nether,
        () => MultiNoiseBiomeSource.NetherBiomes);

    /// <summary>
    /// The end (dimension type <c>minecraft:the_end</c>, noise settings <c>minecraft:end</c>).
    /// </summary>
    public static MojangDimension End { get; } = new(
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

    private readonly Func<IReadOnlyList<BiomeCodec>> possibleBiomes;

    /// <summary>
    /// The dimension's identifier (its level stem's), which is also its level's name.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The biomes the dimension's biome source can return, in vanilla's possibleBiomes order. Its generator orders the
    /// features of these biomes, and only these, together.
    /// </summary>
    public IReadOnlyList<BiomeCodec> PossibleBiomes => this.possibleBiomes();

    /// <summary>
    /// The dimension type's codec, which decides the build range and sky light.
    /// </summary>
    public DimensionCodec DimensionType => field ??= CodecRegistry.TryGetDimension(this.dimensionType, out var codec)
        ? codec!
        : throw new InvalidOperationException($"Unknown dimension type '{this.dimensionType}'.");

    /// <summary>
    /// The noise settings key, one of <c>NoiseRegistry.NoiseSettings.All</c>.
    /// </summary>
    public string NoiseSettings { get; }

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
    public Func<RandomState, IClimateBiomeSource> CreateBiomeSource { get; }

    /// <summary>
    /// Configured carver names (in <c>Assets/configured_carver</c>), in the order the dimension's biomes list them.
    /// </summary>
    /// <remarks>
    /// Vanilla picks the carvers of each start chunk's biome; every vanilla biome of a dimension lists the same ones,
    /// so one list per dimension gives the same result. A plugin's dimension carves like the vanilla dimension whose
    /// noise settings it uses, whatever its biomes list.
    /// </remarks>
    public IReadOnlyList<string> Carvers { get; }

    /// <param name="identifier">The level stem's identifier.</param>
    /// <param name="dimensionType">A registered dimension type.</param>
    /// <param name="noiseSettings">One of vanilla's noise settings.</param>
    /// <param name="createBiomeSource">Creates the biome source for a world.</param>
    /// <param name="possibleBiomes">The biome source's possible biomes, without creating one.</param>
    public MojangDimension(
        string identifier,
        string dimensionType,
        string noiseSettings,
        Func<RandomState, IClimateBiomeSource> createBiomeSource,
        Func<IReadOnlyList<BiomeCodec>> possibleBiomes)
    {
        this.Identifier = identifier;
        this.dimensionType = dimensionType;
        this.NoiseSettings = noiseSettings;
        this.CreateBiomeSource = createBiomeSource;
        this.possibleBiomes = possibleBiomes;
        this.Carvers = CarversOf(noiseSettings);
    }

    // The carvers vanilla's biomes of the dimension using these settings list.
    private static IReadOnlyList<string> CarversOf(string noiseSettings) => noiseSettings switch
    {
        "minecraft:nether" => ["nether_cave"],
        "minecraft:end" => [],
        _ => ["cave", "cave_extra_underground", "canyon"]
    };
}
