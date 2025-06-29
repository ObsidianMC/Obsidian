using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public sealed class BadlandsDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
{
    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (Chunk.X << 4) + Position.X;
        int worldZ = (Chunk.Z << 4) + Position.Z;

        Chunk.SetBlock(Position, BlocksRegistry.RedSand);
        for (int y = -1; y > -15; y--)
        {
            var a = (Position.Y + y) % 15;
            if (a == 15)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.BrownTerracotta);
            else if (a == 14)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.WhiteTerracotta);
            else if (a == 13)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.GrayTerracotta);
            else if (a >= 11)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.YellowTerracotta);
            else if (a == 8 || a == 9)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.RedTerracotta);
            else if (a == 6)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.OrangeTerracotta);
            else if (a == 3)
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.YellowTerracotta);
            else
                Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Terracotta);
        }

        var bushNoise = Noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.03) // 1% chance for bush
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.DeadBush);

        var cactusNoise = Noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.005) // 0.5% chance for cactus
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Cactus);
            Chunk.SetBlock(Position + (0, 2, 0), BlocksRegistry.Cactus);
            Chunk.SetBlock(Position + (0, 3, 0), BlocksRegistry.Cactus);
        }

        var sandNoise = Noise.Decoration.GetValue(worldX * 0.1, 2, worldZ * 0.1);
        if (sandNoise > 0.4 && sandNoise < 0.5)
        {
            Chunk.SetBlock(Position, BlocksRegistry.Sand);
        }
    }
}
