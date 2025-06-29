using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class FrozenRiverDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
{
    public override void Decorate()
    {
        if (Position.Y <= 64)
        {
            Chunk.SetBlock(Position, BlocksRegistry.Gravel);
            for (int y = 63; y > Position.Y; y--)
            {
                Chunk.SetBlock(Position.X, y, Position.Z, BlocksRegistry.Water);
            }
            Chunk.SetBlock(Position.X, 64, Position.Z, BlocksRegistry.Ice);
        }
        else
        {
            Chunk.SetBlock(Position, BlocksRegistry.Sand);
            for (int y = -1; y > -4; y--)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Sand);
        }
    }
}
