namespace Obsidian.WorldData.Decorators;
using Generators;
using Obsidian.API.Registry.Codecs.Biomes;

public class ColdOceanDecorator : OceanDecorator
{

    public ColdOceanDecorator(BiomeCodec biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = false;
        hasKelp = false;
        hasSeaGrass = true;
        primarySurface = clay;
        secondarySurface = dirt;
        tertiarySurface = gravel;
    }
}
