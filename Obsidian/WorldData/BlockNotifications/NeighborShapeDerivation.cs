using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.BlockNotifications;

/// <summary>
/// Derives a state from its six neighbors before placement (vanilla <c>updateFromNeighbourShapes</c>), through live
/// owner hooks and the shared neighbor shape rules. Pistons use it for the block they finish moving.
/// </summary>
/// <remarks>
/// A fluid tick asked for on the way goes through the level, which reads the block still at the position (the moving
/// piston), so none is scheduled from the candidate state. See docs/known-gaps/redstone.md.
/// </remarks>
internal static class NeighborShapeDerivation
{
    // The order of vanilla's state derivation matters when several neighbors affect the same state.
    private static readonly BlockFace[] directions =
        [BlockFace.West, BlockFace.East, BlockFace.North, BlockFace.South, BlockFace.Down, BlockFace.Up];

    internal static IBlock Derive(IBlockChangeWorld world, Vector position, IBlock state)
    {
        var view = new LiveShapeView(world);
        foreach (var direction in directions)
        {
            var neighborPosition = position.Offset(direction);
            var neighbor = world.GetLoadedBlock(neighborPosition) ?? BlocksRegistry.VoidAir;
            var brightness = state.BlockClass() == "MushroomBlock" ? world.GetRawBrightness(position) : 15;
            ShapeUpdater.ScheduleFluidTick(view, state, position, neighbor);
            state = world.UpdateShape(position, state, direction, neighborPosition, neighbor)
                ?? ShapeUpdater.UpdateShape(view, state, position, direction, neighbor,
                    brightness, scheduleFluidTick: false);
        }

        return state;
    }
}
