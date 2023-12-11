﻿using Obsidian.Registries;
using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class OldGrowthBirchForestDecorator : BaseDecorator
{
    public OldGrowthBirchForestDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(6, typeof(BirchTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(TallBirchTree)));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(DandelionFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(PoppyFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(1, typeof(LilyFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(LilacFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(1, typeof(RoseBushFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(PeonyFlora), 4, 3));
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

        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.ShortGrass);
    }
}
