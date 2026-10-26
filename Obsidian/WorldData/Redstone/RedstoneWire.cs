using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Redstone wire (vanilla <c>RedStoneWireBlock</c>): the output it gives its neighbors, and its own power, which it recalculates
/// whenever a neighbor changes. Its shape is kept by <see cref="ShapeUpdater"/>.
/// </summary>
/// <remarks>
/// Power is the steady state of vanilla's power rule, reached through the block notification queue, so the queue's order and
/// budget also apply to wires. A wire's power change notifies its neighbors, which recalculate in turn, the same chain vanilla
/// follows.
/// </remarks>
internal sealed class RedstoneWire(RedstoneEngine engine) : IRedstoneBehavior, INeighborChangedBehavior, IPlacementBehavior
{
    public const string BlockClass = "RedStoneWireBlock";

    private static readonly BlockFace[] horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    /// <summary>
    /// Registers the wire's output and its neighbor reaction.
    /// </summary>
    public static void Register(RedstoneBehaviors redstone, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var wire = new RedstoneWire(engine);
        redstone.Register(BlockClass, wire);
        neighbors.Register(BlockIds.RedstoneWire, wire);
        neighbors.RegisterPlaced(BlockIds.RedstoneWire, wire);
    }

    /// <summary>
    /// Vanilla <c>RedStoneWireBlock.getSignal</c>: nothing below, nothing while the wire works out its power, full power above it, and
    /// horizontally only toward a side it is connected to.
    /// </summary>
    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal)
    {
        if (!wiresGiveSignal || direction == BlockFace.Down)
            return 0;

        var power = Power(state);
        if (power == 0)
            return 0;

        if (direction == BlockFace.Up)
            return power;

        // The receiver is on `direction`, so the wire's face toward it is the opposite one.
        return state.GetProperty(direction.Opposite().PropertyName()) != "none" ? power : 0;
    }

    /// <summary>
    /// Vanilla: a wire's direct signal is its signal.
    /// </summary>
    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        this.GetSignal(state, position, direction, wiresGiveSignal);

    /// <summary>
    /// Recalculates the wire's power after a neighbor changed, and writes it if it changed.
    /// </summary>
    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) =>
        this.UpdatePowerAsync(change.Position, change.State, world);

    /// <summary>
    /// Vanilla's placement path first derives the wire's connection state from all six committed neighbors, then works out its
    /// power. This deliberately uses <see cref="ShapeUpdater.UpdateFromNeighbourShapes"/> rather than maintaining a second set of
    /// wire connection rules here.
    /// </summary>
    public async ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world)
    {
        var state = change.New;
        var shaped = ShapeUpdater.UpdateFromNeighbourShapes(new LiveShapeView(world), state, change.Position);
        if (!shaped.IsSameState(state))
        {
            // Commit shape before evaluating power so connected-side output and all reads see the same state.
            await world.SetBlockAsync(change.Position, shaped);
            state = shaped;
        }

        await this.UpdatePowerAsync(change.Position, state, world);
    }

    /// <summary>
    /// Writes a changed power, then notifies the neighbors of the wire and of each of its six neighbors (vanilla
    /// <c>updateNeighborsOfNeighbors</c>). The second ring matters: a torch on a support beside a wire is only re-checked this way.
    /// </summary>
    private async ValueTask UpdatePowerAsync(Vector position, IBlock state, IBlockChangeWorld world)
    {
        var target = this.CalculateTargetPower(position, world);
        if (target == Power(state))
            return;

        await world.SetBlockAsync(position, state.WithProperty("power", target));
        await world.NotifyNeighborsAsync(position);
        foreach (var face in RedstoneEngine.Directions)
            await world.NotifyNeighborsAsync(position.Offset(face));
    }

    /// <summary>
    /// Vanilla <c>calculateTargetStrength</c>: the best neighbor signal, or the strongest wire reached through the wires beside it minus
    /// one, whichever is higher. Wires beside the position don't feed back into the calculation. Unloaded positions count as air.
    /// </summary>
    private int CalculateTargetPower(Vector position, IBlockChangeWorld world)
    {
        var best = engine.GetBestNeighborSignal(position, wiresGiveSignal: false);
        var strength = 0;
        if (best < RedstoneEngine.MaxSignal)
        {
            var aboveIsConductor = world.GetLoadedBlock(position.Offset(BlockFace.Up))?.IsRedstoneConductor() ?? false;
            foreach (var face in horizontal)
            {
                var neighbor = position.Offset(face);
                var neighborState = world.GetLoadedBlock(neighbor);
                if (neighborState is null)
                    continue;

                strength = Math.Max(strength, WirePower(world, neighbor));

                // A conductor beside the wire lets the wire climb onto it, unless a conductor sits above the wire.
                if (neighborState.IsRedstoneConductor())
                {
                    if (!aboveIsConductor)
                        strength = Math.Max(strength, WirePower(world, neighbor.Offset(BlockFace.Up)));
                }
                else
                {
                    strength = Math.Max(strength, WirePower(world, neighbor.Offset(BlockFace.Down)));
                }
            }
        }

        return Math.Max(best, strength - 1);
    }

    private static int WirePower(IBlockChangeWorld world, Vector position) =>
        world.GetLoadedBlock(position) is IBlock state && state.BlockClass() == BlockClass ? Power(state) : 0;

    private static int Power(IBlock state) => int.Parse(state.GetProperty("power"));
}
