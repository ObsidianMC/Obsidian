using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class FrozenRiverDecorator : BaseDecorator
{
    public FrozenRiverDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y <= 64)
        {
            chunk.SetBlock(pos, BlocksRegistry.Gravel);
            for (int y = 63; y > pos.Y; y--)
            {
                chunk.SetBlock(pos.X, y, pos.Z, BlocksRegistry.Water);
            }
            chunk.SetBlock(pos.X, 64, pos.Z, BlocksRegistry.Ice);
        }
        else
        {
            chunk.SetBlock(pos, BlocksRegistry.Sand);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Sand);
        }
    }
}
