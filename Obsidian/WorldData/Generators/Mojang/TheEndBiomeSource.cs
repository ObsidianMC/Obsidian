using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// The end's biomes: the central island within 1024 blocks of the origin, and outer islands picked by the router's
/// erosion (the island height) at the middle of each section, like vanilla's TheEndBiomeSource.
/// </summary>
internal sealed class TheEndBiomeSource : IClimateBiomeSource
{
    private readonly ClimateSampler climateSampler;
    private readonly BiomeCodec end;
    private readonly BiomeCodec highlands;
    private readonly BiomeCodec midlands;
    private readonly BiomeCodec islands;
    private readonly BiomeCodec barrens;

    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; }

    public TheEndBiomeSource(RandomState randomState)
    {
        this.climateSampler = new ClimateSampler(randomState.Router);
        this.end = GetBiome("minecraft:the_end");
        this.highlands = GetBiome("minecraft:end_highlands");
        this.midlands = GetBiome("minecraft:end_midlands");
        this.islands = GetBiome("minecraft:small_end_islands");
        this.barrens = GetBiome("minecraft:end_barrens");
        this.PossibleBiomes = [this.end, this.highlands, this.midlands, this.islands, this.barrens];
    }

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(this.climateSampler, quartX, quartY, quartZ);

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
