using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class WoodedBadlandsDecorator : BaseDecorator
{
    public WoodedBadlandsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(2, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(7, typeof(DarkOakTree)));
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

        var grass = Registry.GetBlock(9);
        var dirt = Registry.GetBlock(Material.Dirt);

        chunk.SetBlock(pos, grass);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), dirt);

        // Flowers
        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Grass));

        var poppyNoise = noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            chunk.SetBlock(pos, Registry.GetBlock(Material.CoarseDirt));

        var dandyNoise = noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
        if (dandyNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.CobblestoneStairs));

        var cornFlowerNoise = noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
        if (cornFlowerNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Dirt));

        var azureNoise = noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
        if (azureNoise > 1)
            chunk.SetBlock(pos, Registry.GetBlock(Material.DarkOakLeaves));
    }
}
