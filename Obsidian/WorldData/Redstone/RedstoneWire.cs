using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Redstone dust, like vanilla's <c>RedStoneWireBlock</c> with its default wire evaluator: its power is the most
/// power other blocks give it, or one less than the dust it connects to, and it connects to dust beside, above and
/// below it and to signal sources beside it.
/// </summary>
internal sealed class RedstoneWire : RedstoneComponent
{
    // Vanilla's Direction.Plane.HORIZONTAL, which dust connects its sides in.
    private static readonly BlockFace[] horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    // Vanilla's Direction.values().
    private static readonly BlockFace[] all =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    private static readonly IBlock dot = BlocksRegistry.RedstoneWire;

    private static readonly IBlock cross = horizontal.Aggregate(
        BlocksRegistry.RedstoneWire,
        (state, face) => state.WithProperty(face.PropertyName(), "side"));

    /// <summary>
    /// The dust's power, from 0 to 15.
    /// </summary>
    public static int Power(IBlock wire) => int.Parse(wire.GetProperty("power")!);

    /// <summary>
    /// Vanilla <c>getConnectionState</c>: <paramref name="state"/> connected to every side it can; dust connected
    /// on one axis only extends across it, and a dot stays a dot.
    /// </summary>
    public static IBlock ConnectionState(WireView view, IBlock state, Vector position)
    {
        var wasDot = IsDot(state);
        var canClimb = !view.GetBlock(position + Vector.Up).IsRedstoneConductor();
        state = dot.WithProperty("power", state.GetProperty("power")!);
        foreach (var face in horizontal)
            state = state.WithProperty(face.PropertyName(), ConnectingSide(view, position, face, canClimb));

        if (wasDot && IsDot(state))
            return state;

        var north = IsConnected(state, BlockFace.North);
        var south = IsConnected(state, BlockFace.South);
        var east = IsConnected(state, BlockFace.East);
        var west = IsConnected(state, BlockFace.West);
        var noZ = !north && !south;
        var noX = !east && !west;

        if (!west && noZ)
            state = state.WithProperty("west", "side");

        if (!east && noZ)
            state = state.WithProperty("east", "side");

        if (!north && noX)
            state = state.WithProperty("north", "side");

        if (!south && noX)
            state = state.WithProperty("south", "side");

        return state;
    }

    /// <summary>
    /// Vanilla <c>RedStoneWireBlock.updateShape</c>: dust without a block to stand on goes; otherwise its
    /// connections follow the neighbour that changed.
    /// </summary>
    public static IBlock UpdateShape(WireView view, IBlock state, Vector position, BlockFace direction, IBlock neighbor)
    {
        if (direction == BlockFace.Down)
            return CanSurviveOn(neighbor) ? state : BlocksRegistry.Air;

        if (direction == BlockFace.Up)
            return ConnectionState(view, state, position);

        var canClimb = !view.GetBlock(position + Vector.Up).IsRedstoneConductor();
        var side = ConnectingSide(view, position, direction, canClimb);
        if ((side != "none") == IsConnected(state, direction) && !IsCross(state))
            return state.WithProperty(direction.PropertyName(), side);

        var crossed = cross.WithProperty("power", state.GetProperty("power")!);
        return ConnectionState(view, crossed.WithProperty(direction.PropertyName(), side), position);
    }

    /// <summary>
    /// Vanilla <c>canSurviveOn</c>: dust stands on a sturdy top face, or on a hopper.
    /// </summary>
    public static bool CanSurviveOn(IBlock below) => below.IsFaceSturdy(BlockFace.Up) || below.Is(BlockIds.Hopper);

    public override bool IsSignalSource(LevelBlockUpdates level) => level.WiresGiveSignal;

    // Dust powers the block below it and the blocks it connects to.
    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction)
    {
        if (!level.WiresGiveSignal || direction == BlockFace.Down)
            return 0;

        var power = Power(state);
        if (power == 0 || direction == BlockFace.Up)
            return power;

        var connections = ConnectionState(View(level), state, position);
        return IsConnected(connections, direction.Opposite()) ? power : 0;
    }

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        level.WiresGiveSignal ? this.GetSignal(level, position, state, direction) : 0;

    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        var level = placement.Level;
        var state = ConnectionState(View(level), cross, placement.Position);

        return CanSurviveOn(level.GetBlock(placement.Position + Vector.Down)) ? state : null;
    }

    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
        if (oldState.Is(BlockIds.RedstoneWire))
            return;

        UpdatePowerStrength(level, position, state);

        // Vanilla's Direction.Plane.VERTICAL: up, then down.
        level.UpdateNeighbors(position + Vector.Up, dot);
        level.UpdateNeighbors(position + Vector.Down, dot);
        UpdateNeighborsOfNeighboringWires(level, position);
    }

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state)
    {
        foreach (var direction in all)
            level.UpdateNeighbors(position.Offset(direction), dot);

        UpdatePowerStrength(level, position, state);
        UpdateNeighborsOfNeighboringWires(level, position);
    }

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (CanSurviveOn(level.GetBlock(position + Vector.Down)))
            UpdatePowerStrength(level, position, state);
        else
            level.DropAndRemove(position, state);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor) =>
        UpdateShape(View(level), state, position, direction, neighbor);

    // Vanilla's updateIndirectNeighbourShapes: dust connected toward a side that isn't dust updates the dust below
    // and above that side, whose connections climb to it.
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
            if (!IsConnected(state, direction) || level.GetBlock(side).Is(BlockIds.RedstoneWire))
                continue;

            var below = side + Vector.Down;
            if (level.GetBlock(below).Is(BlockIds.RedstoneWire))
                level.ShapeUpdateAt(below, direction.Opposite(), flags, updateLimit);

            var above = side + Vector.Up;
            if (level.GetBlock(above).Is(BlockIds.RedstoneWire))
                level.ShapeUpdateAt(above, direction.Opposite(), flags, updateLimit);
        }
    }

    // Vanilla's useWithoutItem: a dot becomes a cross and a cross a dot, unless connections keep it as it is.
    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        if (!mayBuild || !IsCross(state) && !IsDot(state))
            return false;

        var toggled = (IsCross(state) ? dot : cross).WithProperty("power", state.GetProperty("power")!);
        toggled = ConnectionState(View(level), toggled, position);
        if (toggled.IsSameState(state))
            return false;

        level.Set(position, toggled, BlockUpdateFlags.All);

        // Vanilla's updatesOnShapeChange: a conductor the dust stopped or started pointing into hears of it.
        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            if (IsConnected(state, direction) != IsConnected(toggled, direction)
                && level.GetBlock(side).IsRedstoneConductor())
            {
                level.UpdateNeighborsExcept(side, dot, direction.Opposite());
            }
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>DefaultRedstoneWireEvaluator.updatePowerStrength</c>: dust whose power changed takes it, and the
    /// blocks around it hear of it, in the order vanilla's hash set of their positions gives.
    /// </summary>
    private static void UpdatePowerStrength(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var power = TargetStrength(level, position);
        if (Power(state) == power)
            return;

        if (level.GetBlock(position).IsSameState(state))
            level.Set(position, state.WithProperty("power", power), BlockUpdateFlags.Clients);

        foreach (var updated in HashSetOrder(position))
            level.UpdateNeighbors(updated, dot);
    }

    // Vanilla calculateTargetStrength: the power other blocks give the dust, or one less than its connected dust's.
    private static int TargetStrength(LevelBlockUpdates level, Vector position)
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

        return fromBlocks == 15 ? 15 : Math.Max(fromBlocks, IncomingWireSignal(level, position));
    }

    // Vanilla getIncomingWireSignal: the most power of the dust beside it, and above or below those sides where the
    // dust climbs, less one.
    private static int IncomingWireSignal(LevelBlockUpdates level, Vector position)
    {
        var best = 0;
        var aboveIsConductor = level.GetBlock(position + Vector.Up).IsRedstoneConductor();
        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            var state = level.GetBlock(side);
            best = Math.Max(best, WireSignal(state));

            if (state.IsRedstoneConductor() && !aboveIsConductor)
                best = Math.Max(best, WireSignal(level.GetBlock(side + Vector.Up)));
            else if (!state.IsRedstoneConductor())
                best = Math.Max(best, WireSignal(level.GetBlock(side + Vector.Down)));
        }

        return Math.Max(0, best - 1);
    }

    private static int WireSignal(IBlock state) => state.Is(BlockIds.RedstoneWire) ? Power(state) : 0;

    // Vanilla updateNeighborsOfNeighboringWires: the dust beside it, then the dust above or below those sides.
    private static void UpdateNeighborsOfNeighboringWires(LevelBlockUpdates level, Vector position)
    {
        foreach (var direction in horizontal)
            CheckCornerChangeAt(level, position.Offset(direction));

        foreach (var direction in horizontal)
        {
            var side = position.Offset(direction);
            var corner = level.GetBlock(side).IsRedstoneConductor() ? side + Vector.Up : side + Vector.Down;
            CheckCornerChangeAt(level, corner);
        }
    }

    private static void CheckCornerChangeAt(LevelBlockUpdates level, Vector position)
    {
        if (!level.GetBlock(position).Is(BlockIds.RedstoneWire))
            return;

        level.UpdateNeighbors(position, dot);
        foreach (var direction in all)
            level.UpdateNeighbors(position.Offset(direction), dot);
    }

    /// <summary>
    /// The dust and its six neighbours in the order vanilla's <c>HashSet&lt;BlockPos&gt;</c> of them iterates: by
    /// bucket of a 16-bucket table, then in the order they were added (the dust, then down, up, north, south, west,
    /// east). The order the blocks around changed dust hear of it in decides contraptions, so it has to match.
    /// </summary>
    internal static IEnumerable<Vector> HashSetOrder(Vector position)
    {
        var positions = new Vector[all.Length + 1];
        positions[0] = position;
        for (var index = 0; index < all.Length; index++)
            positions[index + 1] = position.Offset(all[index]);

        // OrderBy is stable, keeping the order they were added within a bucket.
        return positions.OrderBy(JavaHashBucket);
    }

    // Vanilla's Vec3i.hashCode, spread like java.util.HashMap.hash, in a table of 16 buckets.
    private static int JavaHashBucket(Vector position)
    {
        unchecked
        {
            var hash = (position.Y + position.Z * 31) * 31 + position.X;
            hash ^= (int)((uint)hash >> 16);
            return hash & 15;
        }
    }

    // Vanilla getConnectingSide: up to dust on top of the neighbour, side to dust or a signal source beside it or to
    // dust below it, else none.
    private static string ConnectingSide(WireView view, Vector position, BlockFace direction, bool canClimb)
    {
        var neighborPosition = position.Offset(direction);
        var neighbor = view.GetBlock(neighborPosition);
        if (canClimb)
        {
            var climbable = neighbor.BlockClass() is "TrapDoorBlock" or "WeatheringCopperTrapDoorBlock"
                || CanSurviveOn(neighbor);

            if (climbable && ConnectsTo(view, view.GetBlock(neighborPosition + Vector.Up), null))
                return neighbor.IsFaceSturdy(direction.Opposite()) ? "up" : "side";
        }

        var connects = ConnectsTo(view, neighbor, direction)
            || !neighbor.IsRedstoneConductor() && ConnectsTo(view, view.GetBlock(neighborPosition + Vector.Down), null);

        return connects ? "side" : "none";
    }

    // Vanilla shouldConnectTo: dust always; repeaters along their axis, observers from their face, and other signal
    // sources from any side, but only beside the dust (with a direction).
    private static bool ConnectsTo(WireView view, IBlock block, BlockFace? direction)
    {
        if (block.Is(BlockIds.RedstoneWire))
            return true;

        if (direction is not BlockFace side)
            return false;

        if (block.Is(BlockIds.Repeater))
            return FeatureHelpers.SameAxis(FeatureHelpers.ParseFace(block.GetProperty("facing")), side);

        if (block.Is(BlockIds.Observer))
            return FeatureHelpers.ParseFace(block.GetProperty("facing")) == side;

        return view.IsSignalSource(block);
    }

    private static bool IsConnected(IBlock wire, BlockFace side) => wire.GetProperty(side.PropertyName()) != "none";

    private static bool IsDot(IBlock wire) => horizontal.All(face => !IsConnected(wire, face));

    private static bool IsCross(IBlock wire) => horizontal.All(face => IsConnected(wire, face));

    private static WireView View(LevelBlockUpdates level) => new(level.GetBlock, level.IsSignalSource);
}

/// <summary>
/// What redstone dust's connections read: the blocks around, and which of them are signal sources (vanilla's
/// <c>isSignalSource</c>, which plugin blocks answer too on a live level).
/// </summary>
internal readonly record struct WireView(Func<Vector, IBlock> GetBlock, Func<IBlock, bool> IsSignalSource);
