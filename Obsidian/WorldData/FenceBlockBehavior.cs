using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

internal sealed class FenceBlockBehavior : IBlockBehavior, IShapeUpdateBehavior
{
    /// <summary>Generation and placement connect fences with this same rule, which only reshapes.</summary>
    public IShapeUpdateBehavior? SharedPassRule => this;

    private static readonly BlockFace[] sides =
        [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    public IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        var state = context.Block.WithProperty("waterlogged",
            context.Level.GetBlock(context.Position).GetFluid() == FluidKind.Water);
        foreach (var side in sides)
        {
            var neighbor = context.Level.GetBlock(context.Position.Offset(side));
            state = state.WithProperty(side.PropertyName(), Connects(state, neighbor, side.Opposite()));
        }

        return state;
    }

    public IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        if (direction is BlockFace.Up or BlockFace.Down)
            return state;

        return state.WithProperty(direction.PropertyName(), Connects(state, neighbor, direction.Opposite()));
    }

    private static bool Connects(IBlock fence, IBlock neighbor, BlockFace side)
    {
        // Vanilla joins fences in the same wooden/non-wooden tag group, or gates across their facing axis.
        var sameGroup = TagsRegistry.Block.Fences.Entries.Contains(neighbor.RegistryId)
            && TagsRegistry.Block.WoodenFences.Entries.Contains(fence.RegistryId)
                == TagsRegistry.Block.WoodenFences.Entries.Contains(neighbor.RegistryId);
        var sideOnZ = side is BlockFace.North or BlockFace.South;
        var gateOnZ = neighbor.GetProperty("facing") is "north" or "south";
        var gate = neighbor.BlockClass() == "FenceGateBlock" && sideOnZ != gateOnZ;
        // Vanilla's Block.isExceptionForConnection, which tells leaves by class, not by the leaves tag.
        var exception = ShapeRules.ShapeConnections.IsExceptionForConnection(neighbor);
        return sameGroup || gate || !exception && neighbor.IsFaceSturdy(side);
    }
}
