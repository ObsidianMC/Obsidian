namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// What vanilla's world preset generates in a dimension: its noise settings, build range, biome source and carvers.
/// </summary>
internal sealed class MojangDimension
{
    /// <summary>
    /// The overworld (dimension type <c>minecraft:overworld</c>, noise settings <c>minecraft:overworld</c>).
    /// </summary>
    public static MojangDimension Overworld { get; } = new("minecraft:overworld", -64, 384, true, MultiNoiseBiomeSource.Overworld,
        ["cave", "cave_extra_underground", "canyon"]);

    /// <summary>
    /// The nether (dimension type <c>minecraft:the_nether</c>, noise settings <c>minecraft:nether</c>).
    /// </summary>
    public static MojangDimension Nether { get; } = new("minecraft:nether", 0, 256, false, MultiNoiseBiomeSource.Nether, ["nether_cave"]);

    /// <summary>
    /// The end (dimension type <c>minecraft:the_end</c>, noise settings <c>minecraft:end</c>).
    /// </summary>
    public static MojangDimension End { get; } = new("minecraft:end", 0, 256, true, randomState => new TheEndBiomeSource(randomState), []);

    /// <summary>
    /// The noise settings key.
    /// </summary>
    public string NoiseSettings { get; }

    /// <summary>
    /// The dimension type's lowest block Y.
    /// </summary>
    public int MinY { get; }

    /// <summary>
    /// The dimension type's build height in blocks.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Whether the dimension type has sky light (the nether doesn't).
    /// </summary>
    public bool HasSkyLight { get; }

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

    private MojangDimension(string noiseSettings, int minY, int height, bool hasSkyLight,
        Func<RandomState, IClimateBiomeSource> createBiomeSource, IReadOnlyList<string> carvers)
    {
        this.NoiseSettings = noiseSettings;
        this.MinY = minY;
        this.Height = height;
        this.HasSkyLight = hasSkyLight;
        this.CreateBiomeSource = createBiomeSource;
        this.Carvers = carvers;
    }
}
