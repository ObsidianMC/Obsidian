using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class StonyPeaksDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
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
            Chunk.SetBlock(Position, BlocksRegistry.Gravel);
            return;
        }

        int worldX = (Chunk.X << 4) + Position.X;
        int worldZ = (Chunk.Z << 4) + Position.Z;

        var cobbleNoise = Noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (cobbleNoise > 0 && cobbleNoise < 0.5) // 50% chance for grass
            Chunk.SetBlock(Position + (0, -1, 0), BlocksRegistry.Cobblestone);

        var mossNoise = Noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03);
        if (mossNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.MossBlock);

        var clayNoise = Noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03);
        if (clayNoise > 1)
            Chunk.SetBlock(Position + (0, -1, 0), BlocksRegistry.Clay);

        var grassNoise = Noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03);
        if (grassNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.ShortGrass);

        var coalNoise = Noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03);
        if (coalNoise > 1)
            Chunk.SetBlock(Position + (0, -1, 0), BlocksRegistry.CoalOre);

        var ironNoise = Noise.Decoration.GetValue(worldX * 0.03, 13, worldZ * 0.03);
        if (ironNoise > 1)
            Chunk.SetBlock(Position + (0, -1, 0), BlocksRegistry.IronOre);
    }
}
