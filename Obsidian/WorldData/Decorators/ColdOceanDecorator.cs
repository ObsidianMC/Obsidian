namespace Obsidian.WorldData.Decorators;
using Generators;

public class ColdOceanDecorator : OceanDecorator
{

    public ColdOceanDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = false;
        hasKelp = false;
        hasSeaGrass = true;
        primarySurface = clay;
        secondarySurface = dirt;
        tertiarySurface = gravel;
    }
}
