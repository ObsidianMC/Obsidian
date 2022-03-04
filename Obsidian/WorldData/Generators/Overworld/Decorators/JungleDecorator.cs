﻿using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Flora;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class JungleDecorator : BaseDecorator
{
    public JungleDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(6, typeof(JungleTree)));
    }

    public override void Decorate()
    {
        if (pos.Y < noise.settings.WaterLevel)
        {
            FillSand();
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var grass = Registry.GetBlock(9);
        var dirt = Registry.GetBlock(Material.Dirt);

        chunk.SetBlock(pos, grass);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), dirt);


        var grassNoise = noise.Decoration(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Grass));
    }
}
