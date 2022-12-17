using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class OldGrowthSpruceTaigaDecorator : BaseDecorator
{
    public OldGrowthSpruceTaigaDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(SpruceTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, typeof(LargeSpruceTree)));
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

        var grassblock = BlocksRegistry.Get(Material.GrassBlock);
        var dirt = BlocksRegistry.Get(Material.Dirt);
        var podzol = BlocksRegistry.Get(Material.Podzol);

        chunk.SetBlock(pos, grassblock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), dirt);

        if (!chunk.GetBlock(pos + (0, 1, 0)).IsAir) { return; }

        var grass = BlocksRegistry.Get(Material.Grass);
        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.1)
            chunk.SetBlock(pos + (0, 1, 0), grass);

        var dandelion = BlocksRegistry.Get(Material.Dandelion);
        var dandelionNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (dandelionNoise > 0 && dandelionNoise < 0.05)
        {
            chunk.SetBlock(pos + (0, 1, 0), dandelion);
        }

        var coarseDirt = BlocksRegistry.Get(Material.CoarseDirt); //TODO state == 0
        if (noise.Decoration.GetValue(worldX * 0.003, 10, worldZ * 0.003) > 0.5)
        {
            chunk.SetBlock(pos, coarseDirt);
        }

        if (noise.Decoration.GetValue(worldX * 0.003, 18, worldZ * 0.003) > 0.5)
        {
            chunk.SetBlock(pos, podzol);
        }

        var berries = BlocksRegistry.Get(Material.SweetBerryBush); //TODO state == 2
        if (noise.Decoration.GetValue(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
        {
            chunk.SetBlock(pos + (0, 1, 0), berries);
        }
    }
}
