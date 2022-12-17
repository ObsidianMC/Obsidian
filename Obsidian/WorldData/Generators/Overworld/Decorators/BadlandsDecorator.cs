﻿using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class BadlandsDecorator : BaseDecorator
{
    public BadlandsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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

        var sand = BlocksRegistry.Get(Material.RedSand);
        var sand2 = BlocksRegistry.Get(Material.Sand);
        var deadbush = BlocksRegistry.Get(Material.DeadBush);
        var cactus = BlocksRegistry.Get(Material.Cactus);

        chunk.SetBlock(pos, sand);
        for (int y = -1; y > -15; y--)
        {
            //TODO SET BLOCK COLOR
            var a = (pos.Y + y) % 15;
            if (a == 15)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.BrownTerracotta));//Brown
            else if (a == 14)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.WhiteTerracotta));//White
            else if (a == 13)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.GrayTerracotta));//Gray
            else if (a >= 11)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.YellowTerracotta));//Yellow
            else if (a == 8 || a == 9)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.RedTerracotta));//Red
            else if (a == 6)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.OrangeTerracotta));//Orange
            else if (a == 3)
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.YellowTerracotta));//Yellow
            else
                chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Get(Material.Terracotta));
        }

        var bushNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.03) // 1% chance for bush
            chunk.SetBlock(pos + (0, 1, 0), deadbush);

        var cactusNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.005) // 0.5% chance for cactus
        {
            chunk.SetBlock(pos + (0, 1, 0), cactus);
            chunk.SetBlock(pos + (0, 2, 0), cactus);
            chunk.SetBlock(pos + (0, 3, 0), cactus);
        }

        var sandNoise = noise.Decoration.GetValue(worldX * 0.1, 2, worldZ * 0.1);
        if (sandNoise > 0.4 && sandNoise < 0.5)
        {
            chunk.SetBlock(pos, sand2);
        }
    }
}
