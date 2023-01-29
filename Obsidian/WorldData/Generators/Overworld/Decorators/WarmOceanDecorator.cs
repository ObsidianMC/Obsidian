namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class WarmOceanDecorator : OceanDecorator
{

    public WarmOceanDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        primarySurface = sand;
        secondarySurface = dirt;
        hasSeaGrass = hasKelp = true;
        hasMagma = true;
    }
}
