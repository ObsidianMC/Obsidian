using Obsidian.API.Blocks;
using Obsidian.Entities;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A rail's block, like vanilla's <c>BaseRailBlock</c>: placed along the way the player faces, it then turns to join
/// the rails around it, which turn to join it (see <see cref="RailConnections"/>), and a plain rail at a T-junction
/// switches its curve when a redstone source beside it changes.
/// </summary>
internal class RailComponent : RedstoneComponent
{
    // Vanilla's flag 3: neighbours and clients hear of the change.
    private protected const BlockUpdateFlags Update = BlockUpdateFlags.Neighbors | BlockUpdateFlags.Clients;

    // Vanilla's getStateForPlacement: along the player's facing, waterlogged in a water source.
    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        var there = placement.Level.GetBlock(placement.Position);
        var eastWest = placement.HorizontalDirection is BlockFace.East or BlockFace.West;
        var state = Rails.WithShape(placement.Block, eastWest ? RailShape.EastWest : RailShape.NorthSouth);

        return state.WithProperty("waterlogged", there.Is(BlockIds.Water) && there.GetProperty("level") is null or "0");
    }

    // Vanilla's onPlace: a new rail joins up.
    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
        if (oldState.RegistryId != state.RegistryId)
            this.UpdateState(level, position, state);
    }

    // Vanilla's affectNeighborsAfterRemoval: a slope's top neighbours hear of it going, and a straight rail's
    // neighbours and those of the block below.
    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (Rails.IsSlope(Rails.ShapeOf(state)))
            level.UpdateNeighbors(position + Vector.Up, state);

        if (Rails.IsStraight(state))
        {
            level.UpdateNeighbors(position, state);
            level.UpdateNeighbors(position + Vector.Down, state);
        }
    }

    // Vanilla's neighborChanged: an unsupported rail drops (see LevelBlockUpdates.RemoveUnsupportedRail); one that
    // stays updates.
    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state, IBlock source)
    {
        if (!level.GetBlock(position).IsSameState(state) || level.IsUnsupportedRail(position, state))
            return;

        this.OnNeighborChanged(level, position, state, source);
    }

    /// <summary>
    /// Vanilla's <c>updateState(BlockState, Level, BlockPos, Block)</c>: the plain rail's T-junction switch.
    /// </summary>
    private protected virtual void OnNeighborChanged(LevelBlockUpdates level, Vector position, IBlock state, IBlock source)
    {
        if (level.IsSignalSource(BlocksRegistry.GetDefault(source.RegistryId))
            && new RailConnections(level, position, state).CountPotentialConnections() == 3)
        {
            UpdateDirection(level, position, state, alwaysPlace: false);
        }
    }

    /// <summary>
    /// Vanilla's <c>updateState(BlockState, Level, BlockPos, boolean)</c>: the rail turns to join its neighbours, and a
    /// straight rail checks its power.
    /// </summary>
    private protected IBlock UpdateState(LevelBlockUpdates level, Vector position, IBlock state)
    {
        state = UpdateDirection(level, position, state, alwaysPlace: true);
        if (Rails.IsStraight(state))
            this.OnNeighborChanged(level, position, state, state);

        return state;
    }

    // Vanilla's updateDir.
    private static IBlock UpdateDirection(LevelBlockUpdates level, Vector position, IBlock state, bool alwaysPlace) =>
        new RailConnections(level, position, state)
            .Place(level.HasNeighborSignal(position), alwaysPlace, Rails.ShapeOf(state))
            .State;
}

/// <summary>
/// The powered and activator rails' blocks, like vanilla's <c>PoweredRailBlock</c>: powered while a neighbour powers
/// them, or a powered rail of the same kind leads to them within eight rails.
/// </summary>
internal sealed class PoweredRailComponent : RailComponent
{
    // Vanilla's: power travels along at most this many rails.
    private const int MaxDistance = 8;

    private protected override void OnNeighborChanged(LevelBlockUpdates level, Vector position, IBlock state, IBlock source)
    {
        var powered = Rails.IsPowered(state);
        var shouldPower = level.HasNeighborSignal(position)
            || this.FindPoweredRailSignal(level, position, state, forward: true, 0)
            || this.FindPoweredRailSignal(level, position, state, forward: false, 0);

        if (shouldPower == powered)
            return;

        level.Set(position, state.WithProperty("powered", shouldPower), Update);
        level.UpdateNeighbors(position + Vector.Down, state);
        if (Rails.IsSlope(Rails.ShapeOf(state)))
            level.UpdateNeighbors(position + Vector.Up, state);
    }

    // Vanilla's findPoweredRailSignal: the next rail along, or the one under it where the line can step down.
    private bool FindPoweredRailSignal(LevelBlockUpdates level, Vector position, IBlock state, bool forward, int distance)
    {
        if (distance >= MaxDistance)
            return false;

        var (x, y, z) = (position.X, position.Y, position.Z);
        var stepDown = true;
        var shape = Rails.ShapeOf(state);
        switch (shape)
        {
            case RailShape.NorthSouth:
                z += forward ? 1 : -1;
                break;
            case RailShape.EastWest:
                x += forward ? -1 : 1;
                break;
            case RailShape.AscendingEast:
                if (forward)
                {
                    x--;
                }
                else
                {
                    x++;
                    y++;
                    stepDown = false;
                }

                shape = RailShape.EastWest;
                break;
            case RailShape.AscendingWest:
                if (forward)
                {
                    x--;
                    y++;
                    stepDown = false;
                }
                else
                {
                    x++;
                }

                shape = RailShape.EastWest;
                break;
            case RailShape.AscendingNorth:
                if (forward)
                {
                    z++;
                }
                else
                {
                    z--;
                    y++;
                    stepDown = false;
                }

                shape = RailShape.NorthSouth;
                break;
            case RailShape.AscendingSouth:
                if (forward)
                {
                    z++;
                    y++;
                    stepDown = false;
                }
                else
                {
                    z--;
                }

                shape = RailShape.NorthSouth;
                break;
        }

        return this.IsSameRailWithPower(level, new Vector(x, y, z), state, forward, distance, shape)
            || stepDown && this.IsSameRailWithPower(level, new Vector(x, y - 1, z), state, forward, distance, shape);
    }

    // Vanilla's isSameRailWithPower: a powered rail of the same kind, in line, that's powered itself or leads on to
    // power.
    private bool IsSameRailWithPower(
        LevelBlockUpdates level,
        Vector position,
        IBlock kind,
        bool forward,
        int distance,
        RailShape shape)
    {
        var state = level.GetBlock(position);
        if (state.RegistryId != kind.RegistryId)
            return false;

        var railShape = Rails.ShapeOf(state);
        if (shape == RailShape.EastWest
            && railShape is RailShape.NorthSouth or RailShape.AscendingNorth or RailShape.AscendingSouth)
        {
            return false;
        }

        if (shape == RailShape.NorthSouth
            && railShape is RailShape.EastWest or RailShape.AscendingEast or RailShape.AscendingWest)
        {
            return false;
        }

        return Rails.IsPowered(state)
            && (level.HasNeighborSignal(position)
                || this.FindPoweredRailSignal(level, position, state, forward, distance + 1));
    }
}

/// <summary>
/// The detector rail's block, like vanilla's <c>DetectorRailBlock</c>: a minecart on it powers it, strongly to the
/// block below, until a check 20 ticks after the last minecart left; comparators read how full a container minecart on
/// it is, or a command block minecart's successes.
/// </summary>
internal sealed class DetectorRailComponent : RailComponent
{
    // Vanilla's: the rail checks again this often while pressed.
    private const int PressedCheckDelay = 20;

    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        Rails.IsPowered(state) ? 15 : 0;

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        Rails.IsPowered(state) && direction == BlockFace.Up ? 15 : 0;

    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
        if (oldState.RegistryId == state.RegistryId)
            return;

        state = this.UpdateState(level, position, state);
        CheckPressed(level, position, state);
    }

    public override void EntityInside(LevelBlockUpdates level, Vector position, IBlock state, IEntity entity)
    {
        if (!Rails.IsPowered(state))
            CheckPressed(level, position, state);
    }

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (Rails.IsPowered(state))
            CheckPressed(level, position, state);
    }

    // A detector rail's own neighbour changes only matter for its support.
    private protected override void OnNeighborChanged(LevelBlockUpdates level, Vector position, IBlock state, IBlock source)
    {
    }

    /// <summary>
    /// Vanilla's <c>getAnalogOutputSignal</c>: while pressed, a command block minecart's successes, or how full a
    /// container minecart is.
    /// </summary>
    public static int AnalogOutput(LevelBlockUpdates level, IBlock state, Vector position)
    {
        if (!Rails.IsPowered(state))
            return 0;

        var carts = MinecartsOn(level, position).ToArray();
        if (carts.OfType<CommandBlockMinecart>().FirstOrDefault() is CommandBlockMinecart command)
            return command.SuccessCount;

        return carts.OfType<IVehicleContainer>().FirstOrDefault() is IVehicleContainer container
            ? container.RedstoneSignal
            : 0;
    }

    // Vanilla's checkPressed.
    private static void CheckPressed(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (level.IsUnsupportedRail(position, state))
            return;

        var powered = Rails.IsPowered(state);
        var pressed = MinecartsOn(level, position).Any();

        if (pressed != powered)
        {
            var changed = state.WithProperty("powered", pressed);
            level.Set(position, changed, Update);
            UpdatePowerToConnected(level, position, changed);
            level.UpdateNeighbors(position, changed);
            level.UpdateNeighbors(position + Vector.Down, changed);
            state = changed;
        }

        if (pressed)
            level.ScheduleTick(position, state, PressedCheckDelay);

        level.UpdateComparatorsAround(position, state);
    }

    // Vanilla's updatePowerToConnected: the rails it joins hear of it.
    private static void UpdatePowerToConnected(LevelBlockUpdates level, Vector position, IBlock state)
    {
        foreach (var connection in new RailConnections(level, position, state).Connections)
            level.NeighborChangedAt(connection, state, position);
    }

    // Vanilla's getInteractingMinecartOfType: the minecarts in the middle of the rail's block.
    private static IEnumerable<AbstractMinecart> MinecartsOn(LevelBlockUpdates level, Vector position) => level
        .EntitiesIn(
            new VectorD(position.X + 0.2, position.Y, position.Z + 0.2),
            new VectorD(position.X + 0.8, position.Y + 0.8, position.Z + 0.8))
        .OfType<AbstractMinecart>()
        .Where(cart => !cart.IsRemoved);
}
