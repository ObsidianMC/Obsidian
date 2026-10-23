using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Biomes in diagonal stripes of square columns, like vanilla's <c>minecraft:checkerboard</c> biome source.
/// </summary>
internal sealed class CheckerboardBiomeSource : IClimateBiomeSource
{
    private readonly IReadOnlyList<BiomeCodec> biomes;

    // Vanilla shifts quart coordinates by scale + 2, so columns are 2^(scale + 4) blocks wide. Like Java's, C#'s int
    // shift only takes the count's low 5 bits, which matters for scales above 29.
    private readonly int shift;

    /// <param name="biomes">The biomes in their stripe order; may repeat, and mustn't be empty.</param>
    /// <param name="scale">From 0 to 62.</param>
    public CheckerboardBiomeSource(IReadOnlyList<BiomeCodec> biomes, int scale)
    {
        this.biomes = biomes;
        this.shift = scale + 2;
        this.PossibleBiomes = [.. biomes.DistinctBy(biome => biome.Name)];
    }

    public IReadOnlyList<BiomeCodec> PossibleBiomes { get; }

    public BiomeCodec GetNoiseBiome(int quartX, int quartY, int quartZ)
    {
        var stripe = (quartX >> this.shift) + (quartZ >> this.shift);
        var index = stripe % this.biomes.Count;

        // Java's Math.floorMod: negative stripes count down from the end.
        if (index < 0)
            index += this.biomes.Count;

        return this.biomes[index];
    }

    public BiomeCodec GetNoiseBiome(ClimateSampler sampler, int quartX, int quartY, int quartZ) =>
        this.GetNoiseBiome(quartX, quartY, quartZ);
}
