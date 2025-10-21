using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class DeepLukewarmOceanDecorator : OceanDecorator
{

    public DeepLukewarmOceanDecorator(BiomeCodec biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        hasMagma = true;
        hasKelp = false;
        hasSeaGrass = true;
        primarySurface = sand;
        secondarySurface = sand;
        tertiarySurface = gravel;
    }
}
