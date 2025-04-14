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

        if (Position.Y <= Noise.WaterLevel)
        {
            Chunk.SetBlock(Position, BlocksRegistry.Gravel);
        }
        else
        {
            Chunk.SetBlock(Position, BlocksRegistry.Sand);
            for (int y = -1; y > -4; y--)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Gravel);
        }
    }
}
