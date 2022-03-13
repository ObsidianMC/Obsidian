namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class DeepOceanDecorator : OceanDecorator
{

    public DeepOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = false;
        hasKelp = true;
        hasSeaGrass = true;
        primarySurface = sand;
        secondarySurface = dirt;
        tertiarySurface = gravel;
    }
}
