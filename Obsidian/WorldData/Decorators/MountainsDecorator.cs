using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class MountainsDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
{
    public override void Decorate()
    {
        if (Position.Y < 74)
        {
            Chunk.SetBlock(Position, BlocksRegistry.GrassBlock);
            for (int y = Position.Y - 1; y > Position.Y - 5; y--)
            {
                Chunk.SetBlock(Position.X, y, Position.Z, BlocksRegistry.Dirt);
            }
        }

        if (Position.Y > 120)
        {
            Chunk.SetBlock(Position, BlocksRegistry.SnowBlock);
            return;
        }

        int worldX = (Chunk.X << 4) + Position.X;
        int worldZ = (Chunk.Z << 4) + Position.Z;

        var grassNoise = Noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            Chunk.SetBlock(Position, BlocksRegistry.Cobblestone);

        var poppyNoise = Noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.Gravel);

        var dandyNoise = Noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
        if (dandyNoise > 1)
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Clay);

        var cornFlowerNoise = Noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
        if (cornFlowerNoise > 1)
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.ShortGrass);

        var azureNoise = Noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
        if (azureNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.Snow);
    }
}
