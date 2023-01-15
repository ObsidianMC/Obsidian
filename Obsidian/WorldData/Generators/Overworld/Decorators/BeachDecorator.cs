using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class BeachDecorator : BaseDecorator
{
    public BeachDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillWater();
            return;
        }

        for (int y = 0; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Sand);
        for (int y = -4; y > -7; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Sandstone);
    }
}
