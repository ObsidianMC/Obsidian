using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// A level stem generating with vanilla's <c>minecraft:flat</c> generator: layers of blocks in one biome (see
/// <see cref="SuperflatGenerator"/>).
/// </summary>
/// <param name="identifier">The level stem's identifier.</param>
/// <param name="dimensionType">A registered dimension type.</param>
/// <param name="settings">The layers and biome.</param>
internal sealed class FlatStem(string identifier, string dimensionType, FlatLevelSettings settings)
    : LevelStem(identifier, dimensionType)
{
    /// <summary>
    /// The layers and biome.
    /// </summary>
    public FlatLevelSettings Settings { get; } = settings;

    public override IReadOnlyList<BiomeCodec> SourceBiomes => [this.Settings.Biome];

    public override IStemGenerator CreateGenerator() => new FlatStemGenerator(this);
}

/// <summary>
/// The generator of a <see cref="FlatStem"/>'s level.
/// </summary>
internal sealed class FlatStemGenerator(FlatStem stem)
    : SuperflatGenerator(stem.Settings), IStemGenerator
{
    public override string Id => stem.Identifier;

    public LevelStem? Taken { get; private set; }

    public override void Init(ILevel level)
    {
        base.Init(level);

        // The level generates as the files define the stem when it starts, and keeps that (see PluginDimensions.Take).
        if (PluginDimensions.Take(stem) is not FlatStem taken)
            return;

        this.Settings = taken.Settings;
        this.Taken = taken;
    }
}
