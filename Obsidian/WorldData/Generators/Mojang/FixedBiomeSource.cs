using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// One biome everywhere, like vanilla's <c>minecraft:fixed</c> biome source.
/// </summary>
internal sealed class FixedBiomeSource(BiomeCodec biome) : IClimateBiomeSource
{
    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; } = [biome];

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) => biome;

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ) => biome;
}
