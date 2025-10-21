using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class DefaultDecorator(BiomeCodec biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
{
    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }
    }
}
