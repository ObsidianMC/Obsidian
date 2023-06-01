using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class DefaultDecorator : BaseDecorator
{
    public DefaultDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.waterLevel)
        {
            FillWater();
            return;
        }
    }
}
