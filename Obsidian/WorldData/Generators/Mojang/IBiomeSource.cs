using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Chooses the biome stored for each 4x4x4 block cell.
/// </summary>
public interface IBiomeSource
{
    /// <summary>
    /// Gets the biome at quart coordinates (block coordinates divided by 4).
    /// </summary>
    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ);
}

/// <summary>
/// A biome source that reads the noise router's climate, like vanilla's <c>BiomeSource</c>.
/// </summary>
internal interface IClimateBiomeSource : IBiomeSource
{
    /// <summary>
    /// Every biome this source can return, in vanilla's possibleBiomes order (which decides feature ordering).
    /// </summary>
    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; }

    /// <summary>
    /// Gets the biome using another sampler over the same router, e.g. a <see cref="NoiseChunk"/>'s cached one.
    /// </summary>
    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ);
}
