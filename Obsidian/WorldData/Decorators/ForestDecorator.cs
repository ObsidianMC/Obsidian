using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class ForestDecorator : BaseDecorator
{
    private static IBlock sweetBerryBush = BlocksRegistry.Get(Material.SweetBerryBush, new SweetBerryBushStateBuilder().WithAge(3).Build());
    private static IBlock roseBushUpperState = BlocksRegistry.Get(Material.RoseBush, new RoseBushStateBuilder().WithHalf(BlockHalf.Upper).Build());
    private static IBlock peonyUpperState = BlocksRegistry.Get(Material.RoseBush, new RoseBushStateBuilder().WithHalf(BlockHalf.Upper).Build());

    public ForestDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(BirchTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(2, typeof(LargeOakTree)));
    }

    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (Chunk.X << 4) + Position.X;
        int worldZ = (Chunk.Z << 4) + Position.Z;

        Chunk.SetBlock(Position, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Dirt);

        if (!Chunk.GetBlock(Position + (0, 1, 0)).IsAir) { return; }

        var grassNoise = Noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.1)
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.ShortGrass);

        if (Noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03) > 0.8)
        {
            Chunk.SetBlock(Position, BlocksRegistry.Dirt);
        }
        var dandelionNoise = Noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (dandelionNoise > 0 && dandelionNoise < 0.05)
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Dandelion);
            return;
        }

        var peonyNoise = Noise.Decoration.GetValue(worldX * 0.1, 2, worldZ * 0.1);
        if (peonyNoise > 0.65 && peonyNoise < 0.665)
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Peony);
            Chunk.SetBlock(Position + (0, 2, 0), peonyUpperState);
            return;
        }

        var roseNoise = Noise.Decoration.GetValue(worldX * 0.1, 3, worldZ * 0.1);
        if (roseNoise > 0.17 && roseNoise < 0.185)
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.RoseBush);
            Chunk.SetBlock(Position + (0, 2, 0), roseBushUpperState);
            return;
        }

        if (Noise.Decoration.GetValue(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
        {
            Chunk.SetBlock(Position + (0, 1, 0), sweetBerryBush);
        }
    }
}
