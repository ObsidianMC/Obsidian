using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class ErodedBadlandsDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : BaseDecorator(biome, chunk, surfacePos, helper)
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
            //TODO SET BLOCK COLOR
            var a = (Position.Y + y) % 15;
            Chunk.SetBlock(Position + (0, y, 0), a switch
            {
                15 => BlocksRegistry.BrownTerracotta,
                14 => BlocksRegistry.WhiteTerracotta,
                13 => BlocksRegistry.GrayTerracotta,
                >= 11 => BlocksRegistry.YellowTerracotta,
                8 or 9 => BlocksRegistry.RedTerracotta,
                6 => BlocksRegistry.OrangeTerracotta,
                3 => BlocksRegistry.YellowTerracotta,
                _ => BlocksRegistry.Terracotta
            });
        }

        var bushNoise = Noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.01) // 1% chance for bush
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.DeadBush);

        var cactusNoise = Noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.005) // 0.5% chance for cactus
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Cactus);
            Chunk.SetBlock(Position + (0, 2, 0), BlocksRegistry.Cactus);
            Chunk.SetBlock(Position + (0, 3, 0), BlocksRegistry.Cactus);
        }
    }
}
