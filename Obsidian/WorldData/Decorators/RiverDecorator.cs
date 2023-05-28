using Obsidian.Registries;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class RiverDecorator : BaseDecorator
{

    public RiverDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        FillWater();

        if (pos.Y <= noise.Settings.WaterLevel)
        {
            chunk.SetBlock(pos, BlocksRegistry.Gravel);
        }
        else
        {
            chunk.SetBlock(pos, BlocksRegistry.Sand);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Gravel);
        }
    }
}
