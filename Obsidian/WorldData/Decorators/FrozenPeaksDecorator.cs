using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class FrozenPeaksDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
{
    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        for (int y = 0; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.SnowBlock);

        int worldX = (Chunk.X << 4) + Position.X;
        int worldZ = (Chunk.Z << 4) + Position.Z;

        var decorator1 = Noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (decorator1 > 0 && decorator1 < 0.5) // 50% chance for grass
            Chunk.SetBlock(Position, BlocksRegistry.FrostedIce);

        var poppyNoise = Noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.Gravel);
    }
}
