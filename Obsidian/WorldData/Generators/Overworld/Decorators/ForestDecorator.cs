using Obsidian.API.BlockStates.Builders;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class ForestDecorator : BaseDecorator
{
    private static IBlock sweetBerryBush = BlocksRegistry.Get(Material.SweetBerryBush, new SweetBerryBushStateBuilder().WithAge(3).Build());
    private static IBlock roseBushUpperState = BlocksRegistry.Get(Material.RoseBush, new RoseBushStateBuilder().WithHalf(EHalf.Upper).Build());
    private static IBlock peonyUpperState = BlocksRegistry.Get(Material.RoseBush, new RoseBushStateBuilder().WithHalf(EHalf.Upper).Build());

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

        chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Dirt);

        if (!chunk.GetBlock(pos + (0, 1, 0)).IsAir) { return; }

        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Grass);

        if (noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03) > 0.8)
        {
            chunk.SetBlock(pos, BlocksRegistry.Dirt);
        }
        var dandelionNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (dandelionNoise > 0 && dandelionNoise < 0.05)
        {
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Dandelion);
            return;
        }

        var peonyNoise = noise.Decoration.GetValue(worldX * 0.1, 2, worldZ * 0.1);
        if (peonyNoise > 0.65 && peonyNoise < 0.665)
        {
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Peony);
            chunk.SetBlock(pos + (0, 2, 0), peonyUpperState);
            return;
        }

        var roseNoise = noise.Decoration.GetValue(worldX * 0.1, 3, worldZ * 0.1);
        if (roseNoise > 0.17 && roseNoise < 0.185)
        {
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.RoseBush);
            chunk.SetBlock(pos + (0, 2, 0), roseBushUpperState);
            return;
        }

        if (noise.Decoration.GetValue(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
        {
            chunk.SetBlock(pos + (0, 1, 0), sweetBerryBush);
        }
    }
}
