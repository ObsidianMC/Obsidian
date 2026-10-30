using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Shared names and helpers for tripwires and tripwire hooks.
/// </summary>
internal static class TripWireBlocks
{
    public const int WireId = BlockIds.Tripwire;
    public const int HookId = BlockIds.TripwireHook;

    // Vanilla TripWireHookBlock.calculateState scans up to 41 blocks along the hook's facing for the other hook.
    public const int ScanLength = 41;

    // Vanilla TripWireBlock: a powered wire rechecks its entities after 10 ticks.
    public const int PoweredTicks = 10;

    // Vanilla TripWireBlock shapes: attached is a 1/16 to 2.5/16 band; detached is the bottom 8/16.
    public const double AttachedMinY = 1d / 16d;
    public const double AttachedMaxY = 2.5d / 16d;
    public const double DetachedMaxY = 0.5d;

    public static bool IsWire(IBlock? block) => block?.Is(WireId) == true;

    public static bool IsHook(IBlock? block) => block?.Is(HookId) == true;

    public static bool Flag(IBlock block, string property) => block.GetProperty(property) == "true";

    public static BlockFace HookFacing(IBlock hook) => FeatureHelpers.ParseFace(hook.GetProperty("facing"));

    public static string Lower(BlockFace face) => face.ToString().ToLowerInvariant();

    public static Vector Step(Vector position, BlockFace face, int distance)
    {
        var result = position;
        for (var i = 0; i < distance; i++)
            result = result.Offset(face);

        return result;
    }

    public static bool IsHorizontal(BlockFace face) => face is BlockFace.North or BlockFace.South or BlockFace.East or BlockFace.West;
}

/// <summary>
/// The tripwire hook (vanilla <c>TripWireHookBlock</c>). A hook is attached to a sturdy block on the side opposite its facing. It is
/// <c>attached</c> when a line of tripwire reaches the far hook, and <c>powered</c> while a tripwire on that line is pressed. A powered
/// hook gives 15 to the block it is attached to.
/// </summary>
internal sealed class TripWireHook(IRedstoneLevel level) : IRedstoneBehavior, INeighborChangedBehavior, IPlacementBehavior, IRemovalBehavior
{
    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        TripWireBlocks.Flag(state, "powered") ? RedstoneEngine.MaxSignal : 0;

    // The direction runs from the receiver to the hook, so the receiver is the support, on the hook's facing side of it.
    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        TripWireBlocks.Flag(state, "powered") && direction == TripWireBlocks.HookFacing(state) ? RedstoneEngine.MaxSignal : 0;

    // Vanilla onPlace: recalculate the line from the new hook.
    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) =>
        this.CalculateAsync(change.Position, change.New, false, false, -1, null);

    // Vanilla updateShape: a hook whose support stops being sturdy breaks. The break then runs OnRemovedAsync.
    public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        var facing = TripWireBlocks.HookFacing(change.State);
        if (change.Source != change.Position.Offset(facing.Opposite()) || change.SourceState is null)
            return;

        if (!change.SourceState.IsFaceSturdy(facing))
            await level.SetBlockAsync(change.Position, BlocksRegistry.Air);
    }

    // Vanilla onRemove: a removed hook drops its line (and notifies the block it was on, if it was powered).
    public async ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world)
    {
        var powered = TripWireBlocks.Flag(change.Old, "powered");
        if (!powered && !TripWireBlocks.Flag(change.Old, "attached"))
            return;

        var facing = TripWireBlocks.HookFacing(change.Old);
        await this.CalculateAsync(change.Position, change.Old, true, false, -1, null);
        if (powered)
            await this.NotifyAsync(change.Position, facing);
    }

    // Vanilla tick: recalculate, which releases a powered hook whose tripwire is no longer pressed.
    public async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is null || !TripWireBlocks.IsHook(state))
            return;

        await this.CalculateAsync(position, state, false, true, -1, null);
    }

    /// <summary>
    /// Vanilla <c>TripWireHookBlock.calculateState</c>. Scans the line from the hook for the far hook. <paramref name="sourceDistance"/>
    /// is the distance of a tripwire whose state is <paramref name="sourceState"/>, which may not be committed yet.
    /// </summary>
    internal async ValueTask CalculateAsync(Vector position, IBlock state, bool removing, bool notify, int sourceDistance, IBlock? sourceState)
    {
        var facing = TripWireBlocks.HookFacing(state);
        var wasAttached = TripWireBlocks.Flag(state, "attached");
        var attached = !removing;
        var powered = false;
        var farDistance = 0;
        var wires = new IBlock?[TripWireBlocks.ScanLength + 1];

        for (var distance = 1; distance <= TripWireBlocks.ScanLength; distance++)
        {
            var cell = TripWireBlocks.Step(position, facing, distance);
            var block = level.GetLoadedBlock(cell);
            if (TripWireBlocks.IsHook(block))
            {
                if (TripWireBlocks.HookFacing(block!) == facing.Opposite())
                    farDistance = distance;
                break;
            }

            var wire = distance == sourceDistance && sourceState is not null ? sourceState : block;
            if (TripWireBlocks.IsWire(wire))
            {
                var armed = wire!.GetProperty("disarmed") != "true";
                powered |= armed && TripWireBlocks.Flag(wire, "powered");
                wires[distance] = wire;
                if (distance == sourceDistance)
                {
                    // Vanilla: the hook rechecks itself in 10 ticks, and an unarmed wire at the source detaches it.
                    level.ScheduleBlockTick(position, state, TripWireBlocks.PoweredTicks);
                    attached &= armed;
                }
            }
            else
            {
                wires[distance] = null;
                attached = false;
            }
        }

        attached &= farDistance > 1;
        powered &= attached;

        if (farDistance > 0)
        {
            // Vanilla writes the far hook with this hook's new state and the facing pointing back at this one.
            var far = TripWireBlocks.Step(position, facing, farDistance);
            var farState = state.WithProperty("attached", Bool(attached)).WithProperty("powered", Bool(powered))
                .WithProperty("facing", TripWireBlocks.Lower(facing.Opposite()));
            await level.SetBlockAsync(far, farState);
            await this.NotifyAsync(far, facing.Opposite());
        }

        if (!removing)
        {
            var next = state.WithProperty("attached", Bool(attached)).WithProperty("powered", Bool(powered));
            await level.SetBlockAsync(position, next);
            if (notify)
                await this.NotifyAsync(position, facing);
        }

        if (wasAttached != attached)
        {
            // The tripwires between the hooks follow the line's attachment.
            for (var distance = 1; distance < farDistance; distance++)
            {
                var stored = wires[distance];
                var cell = TripWireBlocks.Step(position, facing, distance);
                var live = level.GetLoadedBlock(cell);
                if (stored is not null && (TripWireBlocks.IsWire(live) || TripWireBlocks.IsHook(live)))
                    await level.SetBlockAsync(cell, stored.WithProperty("attached", Bool(attached)));
            }
        }
    }

    // Vanilla notifyNeighbours: the block at the position, and the block on the side it faces away from.
    private async ValueTask NotifyAsync(Vector position, BlockFace direction)
    {
        await level.NotifyNeighborsAsync(position);
        await level.NotifyNeighborsAsync(position.Offset(direction.Opposite()));
    }

    internal static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// Placement (vanilla <c>getStateForPlacement</c>): the hook faces away from the first horizontal side it can be attached to, with
    /// the clicked face first and then the player's yaw. Returns <c>null</c> when no side has a sturdy support, so the placement is refused.
    /// </summary>
    public static async ValueTask<IBlock?> DeriveAsync(IBlock hook, Vector position, BlockFace clickedFace, float yaw, Func<Vector, ValueTask<IBlock?>> read)
    {
        var candidates = new List<BlockFace>();
        if (TripWireBlocks.IsHorizontal(clickedFace))
            candidates.Add(clickedFace);

        candidates.Add(PlacementFacing.FromYaw(yaw).Opposite());
        candidates.AddRange(new[] { BlockFace.North, BlockFace.South, BlockFace.East, BlockFace.West });

        foreach (var facing in candidates.Distinct())
        {
            var support = await read(position.Offset(facing.Opposite()));
            if (support is not null && support.IsFaceSturdy(facing))
                return hook.WithProperty("facing", TripWireBlocks.Lower(facing)).WithProperty("attached", "false").WithProperty("powered", "false");
        }

        return null;
    }
}

/// <summary>
/// The tripwire (vanilla <c>TripWireBlock</c>). Entities that touch an unpowered wire power it, and the wire then powers the hooks along
/// its line. A powered wire rechecks its entities every 10 ticks and releases when none are touching it.
/// </summary>
/// <remarks>
/// Entity contact is taken from the entity positions at the level's detection pass, as pressure plates are. Vanilla checks on entity
/// movement, so a contact can be seen up to one tick later here. Shears disarming (vanilla <c>playerWillDestroy</c>) is not implemented.
/// </remarks>
internal sealed class TripWire(IRedstoneLevel level, TripWireHook hook) : INeighborChangedBehavior, IPlacementBehavior, IRemovalBehavior
{
    private HashSet<Vector> contacts = [];

    /// <summary>
    /// Registers the wire and the hook with the level's behavior sets and block ticks, and returns the wire, which runs entity detection.
    /// </summary>
    public static TripWire Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors)
    {
        var hook = new TripWireHook(level);
        var wire = new TripWire(level, hook);

        redstone.Register(BlocksRegistry.GetDefault(TripWireBlocks.HookId).BlockClass(), hook);
        neighbors.Register(TripWireBlocks.HookId, hook);
        neighbors.RegisterPlaced(TripWireBlocks.HookId, hook);
        neighbors.RegisterRemoved(TripWireBlocks.HookId, hook);
        ticks.Register(TripWireBlocks.HookId, (position, _) => level.EnqueueSimulation(() => hook.TickAsync(position)));

        neighbors.Register(TripWireBlocks.WireId, wire);
        neighbors.RegisterPlaced(TripWireBlocks.WireId, wire);
        neighbors.RegisterRemoved(TripWireBlocks.WireId, wire);
        ticks.Register(TripWireBlocks.WireId, (position, _) => level.EnqueueSimulation(() => wire.TickAsync(position)));

        return wire;
    }

    /// <summary>
    /// Runs one detection pass over the entities that tick this level tick.
    /// </summary>
    public ValueTask UpdateAsync(IEnumerable<IEntity> entities) => this.ApplyContactsAsync(this.ContactsOf(entities));

    /// <summary>
    /// The tripwire cells that a non-spectator entity touches, using the wire's shape for its attachment.
    /// </summary>
    internal List<Vector> ContactsOf(IEnumerable<IEntity> entities)
    {
        var found = new HashSet<Vector>();
        foreach (var entity in entities)
        {
            if (entity is IPlayer { GameMode: GameMode.Spectator })
                continue;

            var box = entity.Dimension.CreateBBFromPosition(entity.Position);
            for (var x = (int)Math.Floor(box.Min.X); x <= (int)Math.Floor(box.Max.X - 1e-7); x++)
            {
                for (var z = (int)Math.Floor(box.Min.Z); z <= (int)Math.Floor(box.Max.Z - 1e-7); z++)
                {
                    for (var y = (int)Math.Floor(box.Min.Y); y <= (int)Math.Floor(box.Max.Y - 1e-7); y++)
                    {
                        var cell = new Vector(x, y, z);
                        var state = level.GetLoadedBlock(cell);
                        if (!TripWireBlocks.IsWire(state))
                            continue;

                        var attached = TripWireBlocks.Flag(state!, "attached");
                        var minY = attached ? TripWireBlocks.AttachedMinY : 0;
                        var maxY = attached ? TripWireBlocks.AttachedMaxY : TripWireBlocks.DetachedMaxY;
                        if (box.Min.Y < y + maxY && box.Max.Y > y + minY)
                            found.Add(cell);
                    }
                }
            }
        }

        return found.ToList();
    }

    /// <summary>
    /// Applies one pass: unpowered wires that are touched become powered (vanilla <c>entityInside</c>). The entity set is kept for the
    /// recheck tick.
    /// </summary>
    internal async ValueTask ApplyContactsAsync(IEnumerable<Vector> touched)
    {
        this.contacts = touched.ToHashSet();
        foreach (var position in this.contacts.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z))
        {
            var state = level.GetLoadedBlock(position);
            if (!TripWireBlocks.IsWire(state) || TripWireBlocks.Flag(state!, "powered"))
                continue;

            await this.UpdateSourceAsync(position, state!, true);
        }
    }

    /// <summary>
    /// Recheck tick (vanilla <c>tick</c>): a powered wire looks at the entities still touching it.
    /// </summary>
    internal async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (!TripWireBlocks.IsWire(state) || !TripWireBlocks.Flag(state!, "powered"))
            return;

        await this.UpdateSourceAsync(position, state!, this.contacts.Contains(position));
    }

    // Vanilla updateSource: a change of the powered state is committed (with neighbor updates), then the line is updated. A powered wire
    // schedules its recheck whether or not the state changed.
    private async ValueTask UpdateSourceAsync(Vector position, IBlock state, bool powered)
    {
        if (TripWireBlocks.Flag(state, "powered") != powered)
        {
            state = state.WithProperty("powered", TripWireHook.Bool(powered));
            await level.SetBlockAsync(position, state);
            await this.UpdateLineAsync(position, state);
        }

        if (powered)
            level.ScheduleBlockTick(position, state, TripWireBlocks.PoweredTicks);
    }

    // Vanilla placement and removal: the hooks at the ends of the line are recalculated from this wire.
    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.UpdateLineAsync(change.Position, change.New);

    public ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world) =>
        this.UpdateLineAsync(change.Position, change.Old.WithProperty("powered", "true"));

    // Vanilla updateSource line walk: from the wire, two horizontal directions (south and west) up to 41 blocks. The first block that
    // isn't tripwire ends the walk; a hook ends it too, and a hook facing back is recalculated with this wire as the source.
    private async ValueTask UpdateLineAsync(Vector position, IBlock wire)
    {
        foreach (var face in new[] { BlockFace.South, BlockFace.West })
        {
            for (var distance = 1; distance <= TripWireBlocks.ScanLength; distance++)
            {
                var cell = TripWireBlocks.Step(position, face, distance);
                var block = level.GetLoadedBlock(cell);
                if (TripWireBlocks.IsHook(block))
                {
                    if (TripWireBlocks.HookFacing(block!) == face.Opposite())
                        await hook.CalculateAsync(cell, block!, false, true, distance, wire);
                    break;
                }

                if (!TripWireBlocks.IsWire(block))
                    break;
            }
        }
    }

    // Vanilla updateShape: a horizontal connection is set by the neighbor in that direction, which is a tripwire, or a hook facing the wire.
    public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        var direction = Direction(change.Position, change.Source);
        if (direction is null)
            return;

        var connected = TripWireBlocks.IsHook(change.SourceState)
            ? TripWireBlocks.HookFacing(change.SourceState!) == direction.Value.Opposite()
            : TripWireBlocks.IsWire(change.SourceState);

        var property = TripWireBlocks.Lower(direction.Value);
        var value = TripWireHook.Bool(connected);
        if (change.State.GetProperty(property) == value)
            return;

        await level.WriteAsync(change.Position, change.State.WithProperty(property, value), notifyNeighbors: false);
    }

    /// <summary>
    /// Placement (vanilla <c>getStateForPlacement</c>): the four horizontal connections come from the neighbors around the wire.
    /// </summary>
    public static async ValueTask<IBlock> DeriveAsync(IBlock wire, Vector position, Func<Vector, ValueTask<IBlock?>> read)
    {
        foreach (var face in new[] { BlockFace.North, BlockFace.South, BlockFace.East, BlockFace.West })
        {
            var neighbor = await read(position.Offset(face));
            var connected = TripWireBlocks.IsHook(neighbor)
                ? TripWireBlocks.HookFacing(neighbor!) == face.Opposite()
                : TripWireBlocks.IsWire(neighbor);
            wire = wire.WithProperty(TripWireBlocks.Lower(face), TripWireHook.Bool(connected));
        }

        return wire;
    }

    private static BlockFace? Direction(Vector from, Vector to)
    {
        foreach (var face in new[] { BlockFace.North, BlockFace.South, BlockFace.East, BlockFace.West })
        {
            if (from.Offset(face) == to)
                return face;
        }

        return null;
    }
}
