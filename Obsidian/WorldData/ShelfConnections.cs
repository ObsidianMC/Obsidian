using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// How shelves join into rows, like vanilla's <c>SideChainPartBlock</c> for <c>ShelfBlock</c>: powered shelves facing
/// the same way connect to the powered shelves beside them, up to three in a row, and the row's ends and middle show
/// in their <c>side_chain</c> property. A powered shelf's use swaps the whole row with the hotbar.
/// </summary>
/// <remarks>
/// Left and right are as one looks at a shelf's front: a shelf facing north has its left to the east. Vanilla's
/// <c>side_chain</c> names which end of the row a shelf is: <c>left</c> the left end, <c>right</c> the right end and
/// <c>center</c> the middle.
/// </remarks>
internal static class ShelfConnections
{
    // Vanilla's getMaxChainLength for shelves.
    private const int MaxRow = 3;

    private const string Unconnected = "unconnected";

    private const string Left = "left";

    private const string Right = "right";

    private const string Center = "center";

    /// <summary>Registers every shelf's reaction to power and to being removed.</summary>
    public static void Register(IRedstoneLevel level, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var power = new PowerBehavior(level, engine);
        var removal = new RemovalBehavior();
        foreach (var id in TagsRegistry.Block.WoodenShelves.Entries)
        {
            neighbors.Register(id, power);
            neighbors.RegisterRemoved(id, removal);
        }
    }

    /// <summary>
    /// Vanilla's <c>getAllBlocksConnectedTo</c>: the shelves of the row the shelf at <paramref name="position"/> is in,
    /// from the left as one looks at them, itself included; none when it isn't powered.
    /// </summary>
    public static IReadOnlyList<Vector> ConnectedTo(AbstractLevel level, Vector position) =>
        ConnectedTo(level.BlockUpdates.GetBlock, position);

    /// <summary>
    /// Vanilla's <c>ShelfBlock.onPlace</c>: a shelf that became powered joins the powered shelves beside it, and one
    /// that isn't lets its neighbours go.
    /// </summary>
    public sealed class Placement : IOnPlaceBehavior
    {
        public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
        {
            var world = new Writer(level.GetBlock, (at, block) => level.SetBlock(at, block));
            if (state.GetProperty("powered") == "true")
                PoweringUp(world, position, state, oldState);
            else
                PoweringDown(world, position, state);
        }
    }

    /// <summary>
    /// Vanilla's <c>ShelfBlock.neighborChanged</c>: the shelf follows the signal it gets; unpowered, it leaves its row.
    /// It plays its activate or deactivate sound and the matching game event, without a source.
    /// </summary>
    private sealed class PowerBehavior(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior
    {
        public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            var signal = engine.HasNeighborSignal(change.Position);
            var powered = change.State.GetProperty("powered") == "true";
            if (powered == signal)
                return;

            var next = change.State.WithProperty("powered", signal);
            if (!signal)
                next = next.WithProperty("side_chain", Unconnected);

            await world.SetBlockAsync(change.Position, next);

            var sound = signal ? "minecraft:block.shelf.activate" : "minecraft:block.shelf.deactivate";
            world.PlaySound(change.Position, sound);

            var gameEvent = signal ? MobGameEvent.BlockActivate : MobGameEvent.BlockDeactivate;
            level.EmitGameEvent(gameEvent, change.Position, null);
        }
    }

    /// <summary>
    /// Vanilla's <c>ShelfBlock.affectNeighborsAfterRemoval</c>: a shelf that's gone lets its neighbours go.
    /// </summary>
    private sealed class RemovalBehavior : IRemovalBehavior
    {
        public ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world)
        {
            var writes = new List<(Vector, IBlock)>();
            var writer = new Writer(world.GetLoadedBlock, (at, block) => writes.Add((at, block)));
            PoweringDown(writer, change.Position, change.Old);

            return WriteAllAsync(world, writes);
        }

        private static async ValueTask WriteAllAsync(IBlockChangeWorld world, List<(Vector, IBlock)> writes)
        {
            foreach (var (position, block) in writes)
                await world.SetBlockAsync(position, block);
        }
    }

    /// <summary>Reads and writes blocks for the chain rules, whichever level they run in.</summary>
    private sealed record Writer(Func<Vector, IBlock?> Read, Action<Vector, IBlock> Write);

    /// <summary>
    /// A neighbour as vanilla's <c>Neighbors</c> reads it, once: a powered shelf facing the same way, with its part, or
    /// nothing to connect to.
    /// </summary>
    private readonly record struct Neighbor(Vector Position, string? Part)
    {
        public bool IsConnectable => this.Part is not null;

        // Vanilla's isUnconnectableOrChainEnd: anything but a row's middle ends the walk along it.
        public bool EndsWalk => this.Part != Center;

        // Vanilla's connectsTowards: a row's middle, or the end the walk is heading for.
        public bool ConnectsTowards(string end) => this.Part == Center || this.Part == end;
    }

    // Vanilla's isConnectable: a wooden shelf that's powered.
    private static bool IsConnectable(IBlock? state) =>
        state is not null
        && TagsRegistry.Block.WoodenShelves.Entries.Contains(state.RegistryId)
        && state.GetProperty("powered") == "true";

    private static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    private static string Part(IBlock state) => state.GetProperty("side_chain") ?? Unconnected;

    private static Neighbor NeighborAt(Func<Vector, IBlock?> read, Vector position, BlockFace facing)
    {
        var state = read(position);
        var joins = IsConnectable(state) && Facing(state!) == facing;
        return new Neighbor(position, joins ? Part(state!) : null);
    }

    private static IReadOnlyList<Vector> ConnectedTo(Func<Vector, IBlock?> read, Vector position)
    {
        var state = read(position);
        if (!IsConnectable(state))
            return [];

        var facing = Facing(state!);
        var row = new List<Vector> { position };

        // Vanilla's addBlocksConnectingTowards, to the left (clockwise from the facing) then the right.
        for (var steps = 1; steps < MaxRow; steps++)
        {
            var neighbor = NeighborAt(read, position.Offset(facing.ClockWise(), steps), facing);
            if (neighbor.ConnectsTowards(Left))
                row.Insert(0, neighbor.Position);

            if (neighbor.EndsWalk)
                break;
        }

        for (var steps = 1; steps < MaxRow; steps++)
        {
            var neighbor = NeighborAt(read, position.Offset(facing.CounterClockWise(), steps), facing);
            if (neighbor.ConnectsTowards(Right))
                row.Add(neighbor.Position);

            if (neighbor.EndsWalk)
                break;
        }

        return row;
    }

    // Vanilla's updateSelfAndNeighborsOnPoweringUp: unless a neighbour is connecting this shelf (it's connected now, or
    // was before), it joins the rows on its left and right while the row stays within three.
    private static void PoweringUp(Writer world, Vector position, IBlock state, IBlock oldState)
    {
        if (!IsConnectable(state))
            return;

        var connecting = Part(state) != Unconnected;
        var wasConnected = IsConnectable(oldState) && Part(oldState) != Unconnected;
        if (connecting || wasConnected)
            return;

        var facing = Facing(state);
        var left = NeighborAt(world.Read, position.Offset(facing.ClockWise()), facing);
        var right = NeighborAt(world.Read, position.Offset(facing.CounterClockWise()), facing);

        var leftRow = left.IsConnectable ? ConnectedTo(world.Read, left.Position).Count : 0;
        var rightRow = right.IsConnectable ? ConnectedTo(world.Read, right.Position).Count : 0;

        var part = Unconnected;
        var length = 1;
        if (CanConnect(leftRow, length))
        {
            part = ConnectedToTheLeft(part);
            SetPart(world, left.Position, ConnectedToTheRight(left.Part!));
            length += leftRow;
        }

        if (CanConnect(rightRow, length))
        {
            part = ConnectedToTheRight(part);
            SetPart(world, right.Position, ConnectedToTheLeft(right.Part!));
        }

        SetPart(world, position, part);
    }

    // Vanilla's updateNeighborsAfterPoweringDown: the neighbours on either side let this shelf go.
    private static void PoweringDown(Writer world, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var left = NeighborAt(world.Read, position.Offset(facing.ClockWise()), facing);
        var right = NeighborAt(world.Read, position.Offset(facing.CounterClockWise()), facing);

        if (left.IsConnectable)
            SetPart(world, left.Position, DisconnectedFromTheRight(left.Part!));

        if (right.IsConnectable)
            SetPart(world, right.Position, DisconnectedFromTheLeft(right.Part!));
    }

    private static bool CanConnect(int rowLength, int length) => rowLength > 0 && length + rowLength <= MaxRow;

    // Vanilla's setPart: writes the shelf there as it is now, with every update, when its part changes.
    private static void SetPart(Writer world, Vector position, string part)
    {
        if (world.Read(position) is not IBlock state || Part(state) == part)
            return;

        world.Write(position, state.WithProperty("side_chain", part));
    }

    // Vanilla's SideChainPart transitions.
    private static string ConnectedToTheRight(string part) => part is Unconnected or Left ? Left : Center;

    private static string ConnectedToTheLeft(string part) => part is Unconnected or Right ? Right : Center;

    private static string DisconnectedFromTheRight(string part) => part is Unconnected or Left ? Unconnected : Right;

    private static string DisconnectedFromTheLeft(string part) => part is Unconnected or Right ? Unconnected : Left;
}
