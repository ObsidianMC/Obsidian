using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class BeachDecorator : BaseDecorator
{
    public BeachDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        for (int y = 0; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Sand);
        for (int y = -4; y > -7; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Sandstone);
    }
}
