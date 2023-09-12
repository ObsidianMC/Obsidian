using Obsidian.Registries;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class ErodedBadlandsDecorator : BaseDecorator
{
    public ErodedBadlandsDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        chunk.SetBlock(pos, BlocksRegistry.RedSand);
        for (int y = -1; y > -15; y--)
        {
            //TODO SET BLOCK COLOR
            var a = (pos.Y + y) % 15;
            chunk.SetBlock(pos + (0, y, 0), a switch
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

        var bushNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.01) // 1% chance for bush
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.DeadBush);

        var cactusNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.005) // 0.5% chance for cactus
        {
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Cactus);
            chunk.SetBlock(pos + (0, 2, 0), BlocksRegistry.Cactus);
            chunk.SetBlock(pos + (0, 3, 0), BlocksRegistry.Cactus);
        }
    }
}
