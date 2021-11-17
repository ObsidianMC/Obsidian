using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class DeepOceanDecorator : OceanDecorator
{

    public DeepOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
    {
        hasMagma = false;
        hasKelp = true;
        hasSeaGrass = true;
        primarySurface = sand;
        secondarySurface = dirt;
        tertiarySurface = gravel;
    }
}
