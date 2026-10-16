using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// The end's biomes: the central island within 1024 blocks of the origin, and outer islands picked by the router's
/// erosion (the island height) at the middle of each section, like vanilla's TheEndBiomeSource.
/// </summary>
internal sealed class TheEndBiomeSource : IClimateBiomeSource
{
    private readonly RandomState randomState;
    private readonly BiomeCodec end;
    private readonly BiomeCodec highlands;
    private readonly BiomeCodec midlands;
    private readonly BiomeCodec islands;
    private readonly BiomeCodec barrens;

    public IReadOnlyList<BiomeCodec> PossibleBiomes => Biomes;

    /// <summary>
    /// The biomes every end source can return, in vanilla's possibleBiomes order.
    /// </summary>
    public static IReadOnlyList<BiomeCodec> Biomes => field ??=
    [
        GetBiome("minecraft:the_end"),
        GetBiome("minecraft:end_highlands"),
        GetBiome("minecraft:end_midlands"),
        GetBiome("minecraft:small_end_islands"),
        GetBiome("minecraft:end_barrens")
    ];

    public TheEndBiomeSource(RandomState randomState)
    {
        this.randomState = randomState;
        this.end = Biomes[0];
        this.highlands = Biomes[1];
        this.midlands = Biomes[2];
        this.islands = Biomes[3];
        this.barrens = Biomes[4];
    }

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(this.randomState.ClimateSampler, quartX, quartY, quartZ);

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ)
    {
        var sectionX = quartX >> 2;
        var sectionZ = quartZ >> 2;
        if ((long)sectionX * sectionX + (long)sectionZ * sectionZ <= 4096L)
            return this.end;

        var erosion = sampler.Erosion.GetValue((sectionX * 2 + 1) * 8, quartY << 2, (sectionZ * 2 + 1) * 8);
        if (erosion > 0.25)
            return this.highlands;

        if (erosion >= -0.0625)
            return this.midlands;

        return erosion < -0.21875 ? this.islands : this.barrens;
    }

    private static BiomeCodec GetBiome(string name) => CodecRegistry.TryGetBiome(name, out var biome)
        ? biome!
        : throw new InvalidOperationException($"Unknown biome '{name}'.");
}
