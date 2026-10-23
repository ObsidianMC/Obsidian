using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>IronBarsBlock.getStateForPlacement</c>: each side connects to a neighbour it attaches to, found from the
/// neighbour's sturdy face and <c>attachsTo</c>. Glass panes and copper bars are <c>IronBarsBlock</c> in vanilla too.
/// </summary>
internal sealed class IronBarsPlacementBehavior : IBlockBehavior
{
    public IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        var level = context.Level;
        var position = context.Position;
        var state = context.Block;
        foreach (var face in new[] { BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West })
        {
            var neighbor = level.GetBlock(position + face.ToVector());
            state = state.WithProperty(face.PropertyName(),
                ShapeUpdater.BarsAttachTo(neighbor, neighbor.IsFaceSturdy(face.Opposite())));
        }

        return state;
    }
}

/// <summary>
/// Vanilla <c>IronBarsBlock.updateShape</c>: a horizontal neighbour change re-decides that side's connection. The
/// state is otherwise kept.
/// </summary>
internal sealed class IronBarsShapeUpdateBehavior : IShapeUpdateBehavior
{
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (direction is not (BlockFace.North or BlockFace.East or BlockFace.South or BlockFace.West))
            return state;

        return state.WithProperty(direction.PropertyName(),
            ShapeUpdater.BarsAttachTo(neighbor, neighbor.IsFaceSturdy(direction.Opposite())));
    }
}
