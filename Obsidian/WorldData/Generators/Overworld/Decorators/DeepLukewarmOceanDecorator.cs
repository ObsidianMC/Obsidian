namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class DeepLukewarmOceanDecorator : OceanDecorator
{

    public DeepLukewarmOceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = true;
        hasKelp = false;
        hasSeaGrass = true;
        primarySurface = sand;
        secondarySurface = sand;
        tertiarySurface = gravel;
    }
}
