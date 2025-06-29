using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class WarmOceanDecorator : OceanDecorator
{

    public WarmOceanDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        primarySurface = sand;
        secondarySurface = dirt;
        hasSeaGrass = hasKelp = true;
        hasMagma = true;
    }
}
