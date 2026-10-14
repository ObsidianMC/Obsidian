using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Redstone dust, with vanilla's default wire evaluator: its power is the most power other blocks give it, or one less
/// than the dust it connects to (see <see cref="WireConnections"/>), and it powers the block below and the blocks it
/// connects to.
/// </summary>
internal sealed class RedstoneWire : RedstoneComponent
{
    // Vanilla's Direction.Plane.HORIZONTAL.
    private static readonly BlockFace[] horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    // Vanilla's Direction.values().
    private static readonly BlockFace[] all =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    private static IBlock Dot => WireConnections.Dot;

    /// <summary>
    /// The dust's power, from 0 to 15.
    /// </summary>
    public static int Power(IBlock wire) => WireConnections.Power(wire);

    // While dust works out its own power, no dust gives any, so that it counts only other blocks' power.
    public override bool IsSignalSource(LevelBlockUpdates level) => level.WiresGiveSignal;

    // Dust powers the block below it and the blocks it connects to.
    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction)
    {
        if (!level.WiresGiveSignal || direction == BlockFace.Down)
            return 0;

        var power = Power(state);
        if (power == 0 || direction == BlockFace.Up)
            return power;

        var connections = WireConnections.Connect(View(level), state, position);
        return WireConnections.IsConnected(connections, direction.Opposite()) ? power : 0;
    }

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        level.WiresGiveSignal ? this.GetSignal(level, position, state, direction) : 0;

    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        var level = placement.Level;
        if (!WireConnections.CanStandOn(level.GetBlock(placement.Position + Vector.Down)))
            return null;

        return WireConnections.Connect(View(level), WireConnections.Cross, placement.Position);
    }

    // New dust takes its power, then the blocks above and below it hear of it, then the dust around.
    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
        if (oldState.Is(BlockIds.RedstoneWire))
            return;

        Settle(level, position, state);

        // Vanilla's Direction.Plane.VERTICAL: up, then down.
        level.UpdateNeighbors(position + Vector.Up, Dot);
        level.UpdateNeighbors(position + Vector.Down, Dot);
        UpdateNearbyWires(level, position);
    }

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state)
    {
        foreach (var direction in all)
            level.UpdateNeighbors(position.Offset(direction), Dot);

        Settle(level, position, state);
        UpdateNearbyWires(level, position);
    }

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (WireConnections.CanStandOn(level.GetBlock(position + Vector.Down)))
            Settle(level, position, state);
        else
            level.DropAndRemove(position, state);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor) =>
        WireConnections.Reconnect(View(level), state, position, direction, neighbor);

    // Dust connected toward a side that isn't dust updates the dust below and above that side, whose connections
    // climb to it, as vanilla's dust does.
    public override void UpdateIndirectNeighborShapes(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockUpdateFlags flags,
        int updateLimit)
    {
        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            if (!WireConnections.IsConnected(state, direction) || level.GetBlock(side).Is(BlockIds.RedstoneWire))
                continue;

            var below = side + Vector.Down;
            if (level.GetBlock(below).Is(BlockIds.RedstoneWire))
                level.ShapeUpdateAt(below, direction.Opposite(), flags, updateLimit);

            var above = side + Vector.Up;
            if (level.GetBlock(above).Is(BlockIds.RedstoneWire))
                level.ShapeUpdateAt(above, direction.Opposite(), flags, updateLimit);
        }
    }

    // A dot becomes a cross and a cross a dot, unless its connections keep it as it is.
    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        var isCross = WireConnections.IsCross(state);
        if (!mayBuild || !isCross && !WireConnections.IsDot(state))
            return false;

        var toggled = (isCross ? Dot : WireConnections.Cross).WithProperty("power", state.GetProperty("power")!);
        toggled = WireConnections.Connect(View(level), toggled, position);
        if (toggled.IsSameState(state))
            return false;

        level.Set(position, toggled, BlockUpdateFlags.All);

        // A conductor the dust stopped or started pointing into hears of it, as vanilla's dust updates.
        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            var wasConnected = WireConnections.IsConnected(state, direction);
            var changed = wasConnected != WireConnections.IsConnected(toggled, direction);
            if (changed && level.GetBlock(side).IsRedstoneConductor())
                level.UpdateNeighborsExcept(side, Dot, direction.Opposite());
        }

        return true;
    }

    /// <summary>
    /// Gives dust the power it gets, if that changed: then the dust and its neighbours hear of it, in the order a
    /// Java hash set of their positions iterates (the dust, then down, up, north, south, west and east, as added).
    /// The order the blocks around changed dust hear of it in decides contraptions, so it's vanilla's.
    /// </summary>
    private static void Settle(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var power = PowerAt(level, position);
        if (Power(state) == power)
            return;

        if (level.GetBlock(position).IsSameState(state))
            level.Set(position, state.WithProperty("power", power), BlockUpdateFlags.Clients);

        List<Vector> updated = [position, .. all.Select(direction => position.Offset(direction))];
        foreach (var neighbor in FeatureHelpers.JavaHashSetOrder(updated))
            level.UpdateNeighbors(neighbor, Dot);
    }

    // The most power other blocks give the dust, or one less than the most of the dust beside it, and above or below
    // those sides where dust climbs: vanilla's formula.
    private static int PowerAt(LevelBlockUpdates level, Vector position)
    {
        int fromBlocks;
        level.WiresGiveSignal = false;
        try
        {
            fromBlocks = level.GetBestNeighborSignal(position);
        }
        finally
        {
            level.WiresGiveSignal = true;
        }

        if (fromBlocks == 15)
            return 15;

        var fromWires = 0;
        var aboveIsConductor = level.GetBlock(position + Vector.Up).IsRedstoneConductor();
        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            var state = level.GetBlock(side);
            fromWires = Math.Max(fromWires, WirePower(state));

            if (!state.IsRedstoneConductor())
                fromWires = Math.Max(fromWires, WirePower(level.GetBlock(side + Vector.Down)));
            else if (!aboveIsConductor)
                fromWires = Math.Max(fromWires, WirePower(level.GetBlock(side + Vector.Up)));
        }

        return Math.Max(fromBlocks, fromWires - 1);
    }

    private static int WirePower(IBlock state) => state.Is(BlockIds.RedstoneWire) ? Power(state) : 0;

    // The dust beside it, then the dust above or below those sides, each with its neighbours, hear of a change to
    // the dust, as vanilla's dust updates.
    private static void UpdateNearbyWires(LevelBlockUpdates level, Vector position)
    {
        foreach (var direction in horizontal)
            UpdateWireAndAround(level, position.Offset(direction));

        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            var corner = level.GetBlock(side).IsRedstoneConductor() ? side + Vector.Up : side + Vector.Down;
            UpdateWireAndAround(level, corner);
        }
    }

    private static void UpdateWireAndAround(LevelBlockUpdates level, Vector position)
    {
        if (!level.GetBlock(position).Is(BlockIds.RedstoneWire))
            return;

        level.UpdateNeighbors(position, Dot);
        foreach (var direction in all)
            level.UpdateNeighbors(position.Offset(direction), Dot);
    }

    private static WireView View(LevelBlockUpdates level) => new(level.GetBlock, level.IsSignalSource);
}
