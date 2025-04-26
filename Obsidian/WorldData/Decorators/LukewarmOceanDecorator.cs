using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class LukewarmOceanDecorator : OceanDecorator
{

    public LukewarmOceanDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        primarySurface = sand;
        secondarySurface = clay;
        tertiarySurface = gravel;
        hasMagma = false;
        hasSeaGrass = hasKelp = true;
    }
}
