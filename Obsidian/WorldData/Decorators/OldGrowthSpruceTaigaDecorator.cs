using Obsidian.API.BlockStates.Builders;
using Obsidian.Registries;
using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class OldGrowthSpruceTaigaDecorator : BaseDecorator
{
    private static readonly IBlock sweetBerryBush = BlocksRegistry.Get(Material.SweetBerryBush, new SweetBerryBushStateBuilder().WithAge(3).Build());
    public OldGrowthSpruceTaigaDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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

        chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Dirt);

        if (!chunk.GetBlock(pos + (0, 1, 0)).IsAir) { return; }

        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Grass);

        var dandelionNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (dandelionNoise > 0 && dandelionNoise < 0.05)
        {
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Dandelion);
        }

        if (noise.Decoration.GetValue(worldX * 0.003, 10, worldZ * 0.003) > 0.5)
        {
            chunk.SetBlock(pos, BlocksRegistry.CoarseDirt);
        }

        if (noise.Decoration.GetValue(worldX * 0.003, 18, worldZ * 0.003) > 0.5)
        {
            chunk.SetBlock(pos, BlocksRegistry.Podzol);
        }

        if (noise.Decoration.GetValue(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
        {
            chunk.SetBlock(pos + (0, 1, 0), sweetBerryBush);
        }
    }
}
