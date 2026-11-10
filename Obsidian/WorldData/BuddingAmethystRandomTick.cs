using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>BuddingAmethystBlock.randomTick</c>: one time in five, budding amethyst picks a face and starts a
/// small bud in the air or water source there, or grows the bud already on that face a stage.
/// </summary>
internal sealed class BuddingAmethystRandomTick : IRandomTickBehavior
{
    // Vanilla's GROWTH_CHANCE.
    private const int GrowthChance = 5;

    // Vanilla's Direction.values(), which the face draw indexes, so the order is vanilla's.
    private static readonly BlockFace[] faces =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (random.NextInt(GrowthChance) != 0)
            return;

        var face = faces[random.NextInt(faces.Length)];
        var target = position.Offset(face);
        var blocks = level.BlockUpdates;
        var current = blocks.GetBlock(target);
        if (NextStage(current, face) is not int stage)
            return;

        // Waterlogged only in a water source: falling water is full, so a bud grows there, but it isn't a source.
        var waterlogged = FluidState.Of(current).Kind == FluidKind.Water;
        var bud = BlocksRegistry.GetDefault(stage)
            .WithProperty("facing", FeatureHelpers.FaceName(face))
            .WithProperty("waterlogged", waterlogged);
        blocks.SetBlock(target, bud);
    }

    // The block the face grows into: a small bud in air or a full water block, or the next stage of a bud facing
    // away on it. Null when nothing grows there.
    private static int? NextStage(IBlock current, BlockFace face)
    {
        if (current.IsAir || current.Is(BlockIds.Water) && FluidState.Of(current).Amount == 8)
            return BlockIds.SmallAmethystBud;

        if (current.GetProperty("facing") != FeatureHelpers.FaceName(face))
            return null;

        return current.RegistryId switch
        {
            BlockIds.SmallAmethystBud => BlockIds.MediumAmethystBud,
            BlockIds.MediumAmethystBud => BlockIds.LargeAmethystBud,
            BlockIds.LargeAmethystBud => BlockIds.AmethystCluster,
            _ => null
        };
    }
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior BuddingAmethystBlock { get; } = new BuddingAmethystRandomTick();
}
