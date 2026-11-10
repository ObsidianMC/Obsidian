using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.ShapeRules;

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
        // Vanilla's bars and panes take the water source they replace.
        var state = context.Block.WithProperty("waterlogged", level.GetBlock(position).GetFluid() == FluidKind.Water);
        foreach (var face in new[] { BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West })
        {
            var neighbor = level.GetBlock(position + face.ToVector());
            state = state.WithProperty(face.PropertyName(),
                ShapeConnections.BarsAttachTo(neighbor, neighbor.IsFaceSturdy(face.Opposite())));
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
    /// <summary>Generation and placement connect bars and panes with this same rule, which only reshapes.</summary>
    public IShapeUpdateBehavior? SharedPassRule => this;

    public IBlock? UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (direction is not (BlockFace.North or BlockFace.East or BlockFace.South or BlockFace.West))
            return state;

        return state.WithProperty(direction.PropertyName(),
            ShapeConnections.BarsAttachTo(neighbor, neighbor.IsFaceSturdy(direction.Opposite())));
    }
}
