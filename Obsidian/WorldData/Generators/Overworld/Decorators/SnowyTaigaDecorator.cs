using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class SnowyTaigaDecorator : BaseDecorator
{
    public SnowyTaigaDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(3, typeof(SpruceTree)));
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

        var grass = BlocksRegistry.Get(Material.Snow);
        var dirt = BlocksRegistry.Get(Material.Dirt);

        chunk.SetBlock(pos, grass);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), dirt);

        var sand = BlocksRegistry.Get(Material.SnowBlock);
        var sandstone = BlocksRegistry.Get(Material.PackedIce);
        var deadbush = BlocksRegistry.Get(Material.Snow);
        var cactus = BlocksRegistry.Get(Material.FrostedIce);

        for (int y = 0; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), sand);
        for (int y = -4; y > -7; y--)
            chunk.SetBlock(pos + (0, y, 0), sandstone);

        var bushNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.05) // 5% chance for bush
            chunk.SetBlock(pos + (0, 1, 0), deadbush);

        var cactusNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.01) // 1% chance for cactus
        {
            chunk.SetBlock(pos + (0, 1, 0), cactus);
        }
    }
}
