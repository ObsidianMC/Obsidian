using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class ForestDecorator : BaseDecorator
{
    public ForestDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(BirchTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(2, typeof(LargeOakTree)));
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

        var grassblock = new Block(Material.GrassBlock, 1);
        var dirt = Registry.GetBlock(Material.Dirt);

        chunk.SetBlock(pos, grassblock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), dirt);

        if (!chunk.GetBlock(pos + (0, 1, 0)).IsAir) { return; }

        var grass = Registry.GetBlock(Material.Grass);
        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.1)
            chunk.SetBlock(pos + (0, 1, 0), grass);

        if (noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03) > 0.8)
        {
            chunk.SetBlock(pos, dirt);
        }

        var dandelion = Registry.GetBlock(Material.Dandelion);
        var dandelionNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (dandelionNoise > 0 && dandelionNoise < 0.05)
        {
            chunk.SetBlock(pos + (0, 1, 0), dandelion);
            return;
        }

        var peony0 = new Block(Material.Peony);
        var peony1 = new Block(Material.Peony, 1);
        var peonyNoise = noise.Decoration.GetValue(worldX * 0.1, 2, worldZ * 0.1);
        if (peonyNoise > 0.65 && peonyNoise < 0.665)
        {
            chunk.SetBlock(pos + (0, 1, 0), peony1);
            chunk.SetBlock(pos + (0, 2, 0), peony0);
            return;
        }

        var rose0 = new Block(Material.RoseBush, 0);
        var rose1 = new Block(Material.RoseBush, 1);
        var roseNoise = noise.Decoration.GetValue(worldX * 0.1, 3, worldZ * 0.1);
        if (roseNoise > 0.17 && roseNoise < 0.185)
        {
            chunk.SetBlock(pos + (0, 1, 0), rose1);
            chunk.SetBlock(pos + (0, 2, 0), rose0);
            return;
        }

        var berries = new Block(Material.SweetBerryBush, 2);
        if (noise.Decoration.GetValue(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
        {
            chunk.SetBlock(pos + (0, 1, 0), berries);
        }
    }
}
