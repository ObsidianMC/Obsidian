using Obsidian.API.Blocks;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>ChorusPlantBlock</c>: its <c>updateShape</c> and <c>tick</c> overrides. A chorus plant connects to the
/// chorus around it and to what holds it from below; one that can't stay is scheduled to break a tick later, and breaks
/// on that tick if it still can't.
/// </summary>
internal sealed class ChorusPlantBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    private static readonly WorldgenTags.BlockGroup supports = WorldgenTags.Blocks("supports_chorus_plant");

    // Vanilla's Direction.Plane.HORIZONTAL, the order canSurvive looks at the sides in.
    private static readonly BlockFace[] sides = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    public IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        if (!CanSurvive(level, position))
        {
            level.ScheduleTick(position, state, 1);
            return null;
        }

        var connects = neighbor.Is(BlockIds.ChorusPlant)
            || neighbor.Is(BlockIds.ChorusFlower)
            || direction == BlockFace.Down && supports.Contains(neighbor);
        return state.WithProperty(FeatureHelpers.FaceName(direction), connects);
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!CanSurvive(level.AsShapeLevel(), position))
            level.DestroyBlock(position, dropItems: true);
    }

    // Vanilla's ChorusPlantBlock.canSurvive: a plant with a block both above and below can't branch sideways; a branch
    // holds on to a plant beside it that stands on a plant or its support; otherwise it needs a plant or support below.
    private static bool CanSurvive(IShapeLevel level, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);
        var aboveAndBelow = !level.GetBlock(position + Vector.Up).IsAir && !below.IsAir;
        foreach (var side in sides)
        {
            var neighborPosition = position.Offset(side);
            if (!level.GetBlock(neighborPosition).Is(BlockIds.ChorusPlant))
                continue;

            if (aboveAndBelow)
                return false;

            if (HoldsChorus(level.GetBlock(neighborPosition + Vector.Down)))
                return true;
        }

        return HoldsChorus(below);
    }

    private static bool HoldsChorus(IBlock block) => block.Is(BlockIds.ChorusPlant) || supports.Contains(block);
}
