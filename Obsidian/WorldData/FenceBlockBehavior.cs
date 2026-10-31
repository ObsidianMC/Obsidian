using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

internal sealed class FenceBlockBehavior : IBlockBehavior, IShapeUpdateBehavior
{
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
        IBlockLevel level,
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
        var exception = TagsRegistry.Block.Leaves.Entries.Contains(neighbor.RegistryId)
            || TagsRegistry.Block.ShulkerBoxes.Entries.Contains(neighbor.RegistryId)
            || neighbor.UnlocalizedName is "minecraft:barrier" or "minecraft:carved_pumpkin"
                or "minecraft:jack_o_lantern" or "minecraft:melon" or "minecraft:pumpkin";
        return sameGroup || gate || !exception && neighbor.IsFaceSturdy(side);
    }
}
