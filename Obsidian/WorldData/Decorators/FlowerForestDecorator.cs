﻿using Obsidian.Registries;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Features.Trees;

namespace Obsidian.WorldData.Decorators;

public class FlowerForestDecorator : BaseDecorator
{
    public FlowerForestDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(3, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(BirchTree)));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(AlliumFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(PoppyFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(DandelionFlora), 5, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(CornflowerFlora), 4, 2));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(AzureBluetFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(PeonyFlora), 3, 2));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(TulipFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(LilyFlora), 5, 2));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(BlueOrchidFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(LilacFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(RoseBushFlora), 2, 3));
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

        chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Dirt);

        // Flowers
        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.ShortGrass);

        if (noise.Decoration.GetValue(worldX * 0.1, 6, worldZ * 0.1) > 0.8)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.SweetBerryBush);

        if (noise.Decoration.GetValue(worldX * 0.1, 7, worldZ * 0.1) > 1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.TallGrass);

    }
}
