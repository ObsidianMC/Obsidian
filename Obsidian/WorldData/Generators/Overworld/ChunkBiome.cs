using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using static Obsidian.API.Noise.VoronoiBiomes;

namespace Obsidian.WorldData.Generators.Overworld;

public enum Temp
{
    hot,
    warm,
    cold,
    freezing
}

public enum Humidity
{
    wet = 0,
    neutral = 1,
    dry = 2
}

public static class ChunkBiome
{
    public static Biomes GetBiome(int worldX, int worldZ, OverworldTerrain noiseGen)
    {
        BaseBiome bn = noiseGen.GetBiome(worldX, worldZ);
        return (Biomes)bn;
    }
}
