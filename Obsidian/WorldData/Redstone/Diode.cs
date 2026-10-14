using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What repeaters and comparators share, like vanilla's <c>DiodeBlock</c>: they take power from behind and their
/// sides, and power the block in front after a delay. <c>facing</c> points to the block behind (their input).
/// </summary>
internal abstract class Diode : RedstoneComponent
{
    // Vanilla's Direction.values().
    private static readonly BlockFace[] all =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>
    /// Vanilla <c>DiodeBlock.isDiode</c>: a repeater or comparator.
    /// </summary>
    public static bool IsDiode(IBlock state) => state.Is(BlockIds.Repeater) || state.Is(BlockIds.Comparator);

    /// <summary>
    /// Vanilla <c>sideInputDiodesOnly</c>: whether only diodes power the sides.
    /// </summary>
    protected abstract bool SideInputDiodesOnly { get; }

    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    // A powered diode powers, strongly, only the block in front of it.
    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsPowered(state) && Facing(state) == direction ? this.OutputSignal(level, position, state) : 0;

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        this.GetSignal(level, position, state, direction);

    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        if (!CanSurviveOn(placement.Level.GetBlock(placement.Position + Vector.Down)))
            return null;

        return placement.Block.WithProperty("facing", placement.HorizontalDirection.Opposite().PropertyName());
    }

    public override void OnPlacedByPlayer(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.ShouldTurnOn(level, position, state))
            level.ScheduleTick(position, state, 1);
    }

    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState) =>
        UpdateNeighborsInFront(level, position, state);

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state) =>
        UpdateNeighborsInFront(level, position, state);

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (CanSurviveOn(level.GetBlock(position + Vector.Down)))
        {
            this.CheckTickOnNeighbor(level, position, state);
            return;
        }

        level.DropAndRemove(position, state);
        foreach (var direction in all)
            level.UpdateNeighbors(position.Offset(direction), state);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor) =>
        direction == BlockFace.Down && !CanSurviveOn(neighbor) ? BlocksRegistry.Air : state;

    // Vanilla DiodeBlock.tick: unless locked, it turns off when no longer powered, and on otherwise, ticking again
    // to turn off when its input went meanwhile.
    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.IsLocked(level, position, state))
            return;

        var powered = IsPowered(state);
        var shouldTurnOn = this.ShouldTurnOn(level, position, state);
        if (powered && !shouldTurnOn)
        {
            level.Set(position, state.WithProperty("powered", false), BlockUpdateFlags.Clients);
        }
        else if (!powered)
        {
            level.Set(position, state.WithProperty("powered", true), BlockUpdateFlags.Clients);
            if (!shouldTurnOn)
                level.ScheduleTick(position, state, this.Delay(state), TickPriority.VeryHigh);
        }
    }

    /// <summary>
    /// The direction the diode's <c>facing</c> property names: toward its input.
    /// </summary>
    protected static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    protected static bool IsPowered(IBlock state) => state.GetProperty("powered") == "true";

    /// <summary>
    /// Vanilla <c>canSurviveOn</c>: a rigid top face below.
    /// </summary>
    protected static bool CanSurviveOn(IBlock below) => below.IsTopRigidSturdy();

    /// <summary>
    /// Vanilla <c>updateNeighborsInFront</c>: the block in front, then its other neighbours.
    /// </summary>
    protected static void UpdateNeighborsInFront(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var front = position.Offset(facing.Opposite());
        level.NeighborChangedAt(front, state, position);
        level.UpdateNeighborsExcept(front, state, facing);
    }

    /// <summary>
    /// Vanilla <c>getDelay</c>, in game ticks.
    /// </summary>
    protected abstract int Delay(IBlock state);

    /// <summary>
    /// Vanilla <c>isLocked</c>.
    /// </summary>
    protected virtual bool IsLocked(LevelBlockUpdates level, Vector position, IBlock state) => false;

    /// <summary>
    /// Vanilla <c>getOutputSignal</c>, while powered.
    /// </summary>
    protected virtual int OutputSignal(LevelBlockUpdates level, Vector position, IBlock state) => 15;

    /// <summary>
    /// Vanilla <c>shouldTurnOn</c>.
    /// </summary>
    protected virtual bool ShouldTurnOn(LevelBlockUpdates level, Vector position, IBlock state) =>
        this.InputSignal(level, position, state) > 0;

    /// <summary>
    /// Vanilla <c>getInputSignal</c>: the power from behind, dust's own power included.
    /// </summary>
    protected virtual int InputSignal(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var behind = position.Offset(facing);
        var signal = level.GetSignal(behind, facing);
        if (signal >= 15)
            return signal;

        var input = level.GetBlock(behind);
        return Math.Max(signal, input.Is(BlockIds.RedstoneWire) ? RedstoneWire.Power(input) : 0);
    }

    /// <summary>
    /// Vanilla <c>getAlternateSignal</c>: the most power its sides get.
    /// </summary>
    protected int AlternateSignal(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var clockWise = FeatureHelpers.ClockWise(facing);
        var counterClockWise = FeatureHelpers.CounterClockWise(facing);

        return Math.Max(
            level.GetControlInputSignal(position.Offset(clockWise), clockWise, this.SideInputDiodesOnly),
            level.GetControlInputSignal(position.Offset(counterClockWise), counterClockWise, this.SideInputDiodesOnly));
    }

    /// <summary>
    /// Vanilla <c>shouldPrioritize</c>: the diode feeds a diode that doesn't continue its line.
    /// </summary>
    protected static bool ShouldPrioritize(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var front = level.GetBlock(position.Offset(facing.Opposite()));

        return IsDiode(front) && Facing(front) != facing;
    }

    // Vanilla DiodeBlock.checkTickOnNeighbor: unless locked, a change of input schedules the diode's tick after its
    // delay, sooner among the ticks due then when it feeds another diode sideways or turns off.
    protected virtual void CheckTickOnNeighbor(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.IsLocked(level, position, state))
            return;

        var powered = IsPowered(state);
        if (powered == this.ShouldTurnOn(level, position, state) || level.WillTickThisTick(position, state))
            return;

        var priority = TickPriority.High;
        if (ShouldPrioritize(level, position, state))
            priority = TickPriority.ExtremelyHigh;
        else if (powered)
            priority = TickPriority.VeryHigh;

        level.ScheduleTick(position, state, this.Delay(state), priority);
    }
}

/// <summary>
/// The repeater, like vanilla's <c>RepeaterBlock</c>: repeats its input at full power after its delay (1 to 4
/// redstone ticks, two game ticks each), and holds its output while a powered diode points into its side.
/// </summary>
internal sealed class Repeater : Diode
{
    protected override bool SideInputDiodesOnly => true;

    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        if (base.GetStateForPlacement(placement) is not IBlock state)
            return null;

        return state.WithProperty("locked", this.IsLocked(placement.Level, placement.Position, state));
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor)
    {
        if (direction == BlockFace.Down && !CanSurviveOn(neighbor))
            return BlocksRegistry.Air;

        // Any change off its axis, above and below included, may lock or unlock it.
        if (FeatureHelpers.SameAxis(direction, Facing(state)))
            return state;

        return state.WithProperty("locked", this.IsLocked(level, position, state));
    }

    // Vanilla's useWithoutItem: the delay cycles.
    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        if (!mayBuild)
            return false;

        var delay = int.Parse(state.GetProperty("delay")!) % 4 + 1;
        level.Set(position, state.WithProperty("delay", delay), BlockUpdateFlags.All);
        return true;
    }

    protected override int Delay(IBlock state) => int.Parse(state.GetProperty("delay")!) * 2;

    protected override bool IsLocked(LevelBlockUpdates level, Vector position, IBlock state) =>
        this.AlternateSignal(level, position, state) > 0;
}
