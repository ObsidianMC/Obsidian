using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// A biome source of a type a plugin registered, read from a level stem (see
/// <see cref="Registries.Codecs.LevelStemData"/>).
/// </summary>
/// <param name="source">The plugin's biome source.</param>
/// <param name="possibleBiomes">Its possible biomes, read when the stem was.</param>
/// <param name="randomState">The world's random state, whose climate the source samples.</param>
internal sealed class PluginBiomeSource(
    BiomeSourceBase source,
    IReadOnlyList<BiomeCodec> possibleBiomes,
    RandomState randomState) : IClimateBiomeSource
{
    private readonly FrozenDictionary<string, BiomeCodec> biomes =
        possibleBiomes.ToFrozenDictionary(biome => biome.Name);

    public IReadOnlyList<BiomeCodec> PossibleBiomes => possibleBiomes;

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(randomState.ClimateSampler, quartX, quartY, quartZ);

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ)
    {
        var name = source.GetBiome(quartX, quartY, quartZ, sampler);

        return this.biomes.TryGetValue(name, out var biome)
            ? biome
            : throw new InvalidOperationException(
                $"{source.Type} gave {name}, which isn't one of its possible biomes.");
    }
}
