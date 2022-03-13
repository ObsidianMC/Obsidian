namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class ColdOceanDecorator : OceanDecorator
{

    public ColdOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = false;
        hasKelp = false;
        hasSeaGrass = true;
        primarySurface = clay;
        secondarySurface = dirt;
        tertiarySurface = gravel;
    }
}
