using Obsidian.API.Noise;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Position-dependent biome temperature, used for snow and frozen ocean decisions during worldgen.
/// </summary>
/// <remarks>
/// Mirrors vanilla's Biome temperature logic, including its fixed-seed noises; float math is intentional.
/// </remarks>
internal static class BiomeTemperature
{
    private static readonly PerlinSimplexNoise temperatureNoise = new(new WorldgenRandom(new LegacyRandomSource(1234L)), [0]);
    private static readonly PerlinSimplexNoise frozenTemperatureNoise = new(new WorldgenRandom(new LegacyRandomSource(3456L)), [-2, -1, 0]);
    private static readonly PerlinSimplexNoise biomeInfoNoise = new(new WorldgenRandom(new LegacyRandomSource(2345L)), [0]);

    public static bool ColdEnoughToSnow(BiomeCodec biome, int x, int y, int z, int seaLevel) =>
        !(GetTemperature(biome, x, y, z, seaLevel) >= 0.15f);

    public static bool ShouldMeltFrozenOceanIcebergSlightly(BiomeCodec biome, int x, int y, int z, int seaLevel) =>
        GetTemperature(biome, x, y, z, seaLevel) > 0.1f;

    public static float GetTemperature(BiomeCodec biome, int x, int y, int z, int seaLevel)
    {
        var temperature = ModifyTemperature(biome, x, z, biome.Element.Temperature);
        var snowLine = seaLevel + 17;

        if (y <= snowLine)
            return temperature;

        var noise = (float)(temperatureNoise.GetValue(x / 8.0f, z / 8.0f, false) * 8.0);
        return temperature - (noise + y - snowLine) * 0.05f / 40.0f;
    }

    private static float ModifyTemperature(BiomeCodec biome, int x, int z, float temperature)
    {
        if (biome.Element.TemperatureModifier != "frozen")
            return temperature;

        var frozen = frozenTemperatureNoise.GetValue(x * 0.05, z * 0.05, false) * 7.0;
        var info = biomeInfoNoise.GetValue(x * 0.2, z * 0.2, false);

        if (frozen + info < 0.3 && biomeInfoNoise.GetValue(x * 0.09, z * 0.09, false) < 0.8)
            return 0.2f;

        return temperature;
    }
}
