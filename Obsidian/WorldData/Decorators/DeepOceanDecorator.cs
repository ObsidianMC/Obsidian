using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class DeepOceanDecorator : OceanDecorator
{

    public DeepOceanDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = false;
        hasKelp = true;
        hasSeaGrass = true;
        primarySurface = sand;
        secondarySurface = dirt;
        tertiarySurface = gravel;
    }
}
