using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public sealed class BadlandsDecorator : BaseDecorator
{
    public BadlandsDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        chunk.SetBlock(pos, BlocksRegistry.RedSand);
        for (int y = -1; y > -15; y--)
        {
            var a = (pos.Y + y) % 15;
            if (a == 15)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.BrownTerracotta);
            else if (a == 14)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.WhiteTerracotta);
            else if (a == 13)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.GrayTerracotta);
            else if (a >= 11)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.YellowTerracotta);
            else if (a == 8 || a == 9)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.RedTerracotta);
            else if (a == 6)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.OrangeTerracotta);
            else if (a == 3)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.YellowTerracotta);
            else
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Terracotta);
        }

        var bushNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.03) // 1% chance for bush
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.DeadBush);

        var cactusNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.005) // 0.5% chance for cactus
        {
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Cactus);
            chunk.SetBlock(pos + (0, 2, 0), BlocksRegistry.Cactus);
            chunk.SetBlock(pos + (0, 3, 0), BlocksRegistry.Cactus);
        }

        var sandNoise = noise.Decoration.GetValue(worldX * 0.1, 2, worldZ * 0.1);
        if (sandNoise > 0.4 && sandNoise < 0.5)
        {
            chunk.SetBlock(pos, BlocksRegistry.Sand);
        }
    }
}
