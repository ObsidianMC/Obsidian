namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class LukewarmOceanDecorator : OceanDecorator
{

    public LukewarmOceanDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        primarySurface = sand;
        secondarySurface = clay;
        tertiarySurface = gravel;
        hasMagma = false;
        hasSeaGrass = hasKelp = true;
    }
}
