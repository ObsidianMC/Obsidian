using Obsidian.API.Registry.Codecs.Dimensions;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// What vanilla's world preset generates in a dimension: its dimension type, noise settings, biome source and carvers.
/// </summary>
internal sealed class MojangDimension
{
    /// <summary>
    /// The overworld (dimension type <c>minecraft:overworld</c>, noise settings <c>minecraft:overworld</c>).
    /// </summary>
    public static MojangDimension Overworld { get; } = new("minecraft:overworld", "minecraft:overworld", MultiNoiseBiomeSource.Overworld,
        ["cave", "cave_extra_underground", "canyon"]);

    /// <summary>
    /// The nether (dimension type <c>minecraft:the_nether</c>, noise settings <c>minecraft:nether</c>).
    /// </summary>
    public static MojangDimension Nether { get; } = new("minecraft:the_nether", "minecraft:nether", MultiNoiseBiomeSource.Nether,
        ["nether_cave"]);

    /// <summary>
    /// The end (dimension type <c>minecraft:the_end</c>, noise settings <c>minecraft:end</c>).
    /// </summary>
    public static MojangDimension End { get; } = new("minecraft:the_end", "minecraft:end", randomState => new TheEndBiomeSource(randomState), []);

    private readonly string dimensionType;

    /// <summary>
    /// The dimension type's codec, which decides the build range and sky light.
    /// </summary>
    public DimensionCodec DimensionType => field ??= CodecRegistry.TryGetDimension(this.dimensionType, out var codec)
        ? codec!
        : throw new InvalidOperationException($"Unknown dimension type '{this.dimensionType}'.");

    /// <summary>
    /// The noise settings key.
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
    /// so one list per dimension gives the same result.
    /// </remarks>
    public IReadOnlyList<string> Carvers { get; }

    private MojangDimension(string dimensionType, string noiseSettings, Func<RandomState, IClimateBiomeSource> createBiomeSource,
        IReadOnlyList<string> carvers)
    {
        this.dimensionType = dimensionType;
        this.NoiseSettings = noiseSettings;
        this.CreateBiomeSource = createBiomeSource;
        this.Carvers = carvers;
    }
}
