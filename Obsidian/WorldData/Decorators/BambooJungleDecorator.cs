﻿using Obsidian.Registries;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Features.Trees;

namespace Obsidian.WorldData.Decorators;

public class BambooJungleDecorator : BaseDecorator
{
    public BambooJungleDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {

        Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, typeof(JungleTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(7, typeof(LargeJungleTree)));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(LargeFernFlora), 6, 4));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(FernFlora), 6, 4));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(1, typeof(MelonFlora), 6, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(20, typeof(JungleBushFlora), 3, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(BambooFlora), 6, 4));
    }

    public override void Decorate()
    {
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillWater();
            return;
        }

        chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Dirt);

    }
}
