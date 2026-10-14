using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Entities;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What its sides do to a diode.
/// </summary>
internal enum DiodeSides
{
    /// <summary>
    /// A powered diode pointing into a side holds the output as it is (the repeater's lock). Only diodes count.
    /// </summary>
    Lock,

    /// <summary>
    /// The sides' power is compared with the input, or subtracted from it (the comparator's). Any source counts.
    /// </summary>
    Compare
}

/// <summary>
/// The tick priorities a diode schedules its change with. Vanilla runs ticks due on the same game tick by priority,
/// so these decide which of two diodes due together reads the other's new output.
/// </summary>
/// <param name="FeedingDiode">
/// When the block in front is a diode that doesn't point its output back at it: the next diode of a line, or one it
/// feeds from the side.
/// </param>
/// <param name="TurningOff">Otherwise, when it's powered.</param>
/// <param name="Otherwise">Otherwise.</param>
internal readonly record struct DiodePriorities(
    TickPriority FeedingDiode,
    TickPriority TurningOff,
    TickPriority Otherwise);

/// <summary>
/// What a kind of diode is: its delay, what its sides do, what it reads behind it and how it keeps its output.
/// </summary>
/// <remarks>
/// The repeater's and comparator's delays and priorities are vanilla's, which circuits' timing depends on.
/// </remarks>
internal sealed record DiodeKind
{
    /// <summary>
    /// The repeater: full power out while its input is on, 1 to 4 redstone ticks (two game ticks each) later, held
    /// for at least that long, and locked by powered diodes at its sides.
    /// </summary>
    public static DiodeKind Repeater { get; } = new()
    {
        Delay = state => int.Parse(state.GetProperty("delay")!) * 2,
        Sides = DiodeSides.Lock,
        ReadsAnalog = false,
        KeepsStrength = false,
        Priorities = new(TickPriority.ExtremelyHigh, TickPriority.VeryHigh, TickPriority.High)
    };

    /// <summary>
    /// The comparator: its input's strength (analog outputs included) compared with its sides', or less theirs when
    /// subtracting, two game ticks later, kept in its block entity.
    /// </summary>
    public static DiodeKind Comparator { get; } = new()
    {
        Delay = _ => 2,
        Sides = DiodeSides.Compare,
        ReadsAnalog = true,
        KeepsStrength = true,
        Priorities = new(TickPriority.High, TickPriority.Normal, TickPriority.Normal)
    };

    /// <summary>
    /// Game ticks from a change of its inputs to the change of its output, for a state of the diode.
    /// </summary>
    public required Func<IBlock, int> Delay { get; init; }

    /// <summary>
    /// What its sides do.
    /// </summary>
    public required DiodeSides Sides { get; init; }

    /// <summary>
    /// Whether it reads the analog output of the block behind it (a container's fullness, say), or of the block
    /// behind a conductor there, or an item frame on that, rather than the power there.
    /// </summary>
    public required bool ReadsAnalog { get; init; }

    /// <summary>
    /// Whether it keeps its output's strength (in its <see cref="ComparatorBlockEntity"/>) and takes a new one on
    /// each tick. Without, it gives full power while powered and, once on, stays on for at least its delay.
    /// </summary>
    public required bool KeepsStrength { get; init; }

    /// <summary>
    /// The priorities it schedules its ticks with.
    /// </summary>
    public required DiodePriorities Priorities { get; init; }
}

/// <summary>
/// A repeater or comparator (see <see cref="DiodeKind"/>): it takes power from behind and its sides, and powers,
/// strongly, the block in front after its delay. Its <c>facing</c> points to the block behind (its input).
/// </summary>
/// <remarks>
/// A diode's output is a strength: a repeater's is full or none. Its state's <c>powered</c> is whether that strength
/// is above zero, so one rule decides both when it has to change.
/// </remarks>
internal sealed class Diode(DiodeKind kind) : RedstoneComponent
{
    // Vanilla's Direction.values(), the order a diode popping off updates around it in.
    private static readonly BlockFace[] all =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>
    /// Whether a block is a repeater or comparator.
    /// </summary>
    public static bool IsDiode(IBlock state) => state.Is(BlockIds.Repeater) || state.Is(BlockIds.Comparator);

    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    // It powers, weakly and strongly, only the block in front of it.
    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsPowered(state) && Facing(state) == direction ? this.Strength(level, position) : 0;

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        this.GetSignal(level, position, state, direction);

    // Facing away from the player, on a rigid top face; a repeater placed beside a powered diode starts locked.
    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        if (!CanStandOn(placement.Level.GetBlock(placement.Position + Vector.Down)))
            return null;

        var facing = placement.HorizontalDirection.Opposite().PropertyName();
        var state = placement.Block.WithProperty("facing", facing);
        if (kind.Sides == DiodeSides.Lock)
            state = state.WithProperty("locked", this.IsLocked(placement.Level, placement.Position, state));

        return state;
    }

    // Like vanilla's setPlacedBy: placed with its input on, it turns on a game tick later.
    public override void OnPlacedByPlayer(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.Target(level, position, state) > 0)
            level.ScheduleTick(position, state, 1);
    }

    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState) =>
        UpdateFront(level, position, state);

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state) =>
        UpdateFront(level, position, state);

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (CanStandOn(level.GetBlock(position + Vector.Down)))
        {
            this.ScheduleIfChanging(level, position, state);
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
        IBlock neighbor)
    {
        if (direction == BlockFace.Down && !CanStandOn(neighbor))
            return BlocksRegistry.Air;

        // Any change off a repeater's axis, above and below included, may lock or unlock it.
        if (kind.Sides != DiodeSides.Lock || FeatureHelpers.SameAxis(direction, Facing(state)))
            return state;

        return state.WithProperty("locked", this.IsLocked(level, position, state));
    }

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (kind.KeepsStrength)
            this.TakeNewStrength(level, position, state);
        else
            this.Step(level, position, state);
    }

    // A repeater's delay cycles; a comparator switches between comparing and subtracting, with a click.
    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        if (!mayBuild)
            return false;

        if (!kind.KeepsStrength)
        {
            var delay = int.Parse(state.GetProperty("delay")!) % 4 + 1;
            level.Set(position, state.WithProperty("delay", delay), BlockUpdateFlags.All);
            return true;
        }

        var subtract = !IsSubtracting(state);
        var switched = state.WithProperty("mode", subtract ? "subtract" : "compare");
        level.PlaySound(position, "minecraft:block.comparator.click", 0.3f, subtract ? 0.55f : 0.5f, player);
        level.Set(position, switched, BlockUpdateFlags.Clients);
        this.TakeNewStrength(level, position, switched);
        return true;
    }

    /// <summary>
    /// The direction the diode's <c>facing</c> names: toward its input.
    /// </summary>
    private static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    private static bool IsPowered(IBlock state) => state.GetProperty("powered") == "true";

    private static bool IsSubtracting(IBlock state) => state.GetProperty("mode") == "subtract";

    // A diode needs a rigid top face below.
    private static bool CanStandOn(IBlock below) => below.IsTopRigidSturdy();

    // The block in front hears of the diode first, then that block's other neighbours, as vanilla's diodes update.
    private static void UpdateFront(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var front = position.Offset(facing.Opposite());
        level.NeighborChangedAt(front, state, position);
        level.UpdateNeighborsExcept(front, state, facing);
    }

    // The strength it gives while powered: full for a repeater, the kept one for a comparator.
    private int Strength(LevelBlockUpdates level, Vector position)
    {
        if (!kind.KeepsStrength)
            return 15;

        return level.GetBlockEntity(position) is ComparatorBlockEntity comparator ? comparator.OutputSignal : 0;
    }

    // The strength it gives now: its powered state's for a repeater.
    private int CurrentStrength(LevelBlockUpdates level, Vector position, IBlock state) =>
        kind.KeepsStrength ? this.Strength(level, position) : IsPowered(state) ? 15 : 0;

    // The strength its inputs ask for. A repeater's is full while its input is on. A comparator's is its input's,
    // none when its sides are stronger, and less theirs when subtracting; vanilla's formula, which circuits rely on.
    private int Target(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var input = this.Input(level, position, state);
        if (kind.Sides == DiodeSides.Lock)
            return input > 0 ? 15 : 0;

        if (input == 0)
            return 0;

        var sides = this.SidePower(level, position, state);
        if (sides > input)
            return 0;

        return IsSubtracting(state) ? input - sides : input;
    }

    private bool IsLocked(LevelBlockUpdates level, Vector position, IBlock state) =>
        kind.Sides == DiodeSides.Lock && this.SidePower(level, position, state) > 0;

    // The power behind it, dust's own power included. A comparator reads instead the analog output of the block
    // behind it, or (unless that's a fully powered conductor) of the block behind that, or of an item frame on it.
    private int Input(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var behind = position.Offset(facing);
        var signal = level.GetSignal(behind, facing);
        var input = level.GetBlock(behind);
        if (signal < 15 && input.Is(BlockIds.RedstoneWire))
            signal = Math.Max(signal, RedstoneWire.Power(input));

        if (!kind.ReadsAnalog)
            return signal;

        if (level.HasAnalogOutput(input))
            return level.AnalogOutputOf(input, behind, facing.Opposite());

        if (signal >= 15 || !input.IsRedstoneConductor())
            return signal;

        var further = behind.Offset(facing);
        var block = level.GetBlock(further);
        var analog = int.MinValue;
        if (ItemFrameAt(level, facing, further) is ItemFrame frame)
            analog = frame.Item is { IsAir: false } ? frame.Rotation % 8 + 1 : 0;

        if (level.HasAnalogOutput(block))
            analog = Math.Max(analog, level.AnalogOutputOf(block, further, facing.Opposite()));

        return analog != int.MinValue ? analog : signal;
    }

    // The most power its two sides get. A repeater's sides only take a diode's power; a comparator's take any
    // source's, dust's own power and a block of redstone's full power.
    private int SidePower(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var facing = Facing(state);
        var clockWise = FeatureHelpers.ClockWise(facing);
        var counterClockWise = FeatureHelpers.CounterClockWise(facing);

        return Math.Max(
            this.PowerAtSide(level, position.Offset(clockWise), clockWise),
            this.PowerAtSide(level, position.Offset(counterClockWise), counterClockWise));
    }

    private int PowerAtSide(LevelBlockUpdates level, Vector side, BlockFace direction)
    {
        var state = level.GetBlock(side);
        if (kind.Sides == DiodeSides.Lock)
            return IsDiode(state) ? level.GetDirectSignal(side, direction) : 0;

        if (state.Is(BlockIds.RedstoneBlock))
            return 15;

        if (state.Is(BlockIds.RedstoneWire))
            return RedstoneWire.Power(state);

        return level.IsSignalSource(state) ? level.GetDirectSignal(side, direction) : 0;
    }

    // When its output would change, the diode ticks after its delay, unless locked or due to tick this game tick.
    // The priority is vanilla's (see DiodePriorities). The diode in front feeds back into this one when its facing
    // (toward its input) is this one's output direction; any other way, this one feeds it and goes first.
    private void ScheduleIfChanging(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.IsLocked(level, position, state) || level.WillTickThisTick(position, state))
            return;

        var target = this.Target(level, position, state);
        var powered = IsPowered(state);
        if (target == this.CurrentStrength(level, position, state) && powered == target > 0)
            return;

        var facing = Facing(state);
        var front = level.GetBlock(position.Offset(facing.Opposite()));
        var priority = kind.Priorities.Otherwise;
        if (IsDiode(front) && Facing(front) != facing.Opposite())
            priority = kind.Priorities.FeedingDiode;
        else if (powered)
            priority = kind.Priorities.TurningOff;

        level.ScheduleTick(position, state, kind.Delay(state), priority);
    }

    // A repeater's tick, unless locked: it turns off when its input is off, and on otherwise, then ticks again after
    // its delay to turn off if its input went meanwhile, so that it holds a short pulse for its delay, like vanilla's.
    private void Step(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.IsLocked(level, position, state))
            return;

        var powered = IsPowered(state);
        var on = this.Target(level, position, state) > 0;
        if (powered && !on)
        {
            level.Set(position, state.WithProperty("powered", false), BlockUpdateFlags.Clients);
        }
        else if (!powered)
        {
            level.Set(position, state.WithProperty("powered", true), BlockUpdateFlags.Clients);
            if (!on)
                level.ScheduleTick(position, state, kind.Delay(state), kind.Priorities.TurningOff);
        }
    }

    // A comparator's tick: it keeps the strength its inputs ask for, turns on or off with it, and the block in front
    // hears of it. Like vanilla's, a subtracting comparator whose strength stays is left alone.
    private void TakeNewStrength(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var target = this.Target(level, position, state);
        var previous = 0;
        if (level.GetBlockEntity(position) is ComparatorBlockEntity comparator)
        {
            previous = comparator.OutputSignal;
            comparator.OutputSignal = target;
        }

        if (previous == target && IsSubtracting(state))
            return;

        var powered = IsPowered(state);
        if (powered != target > 0)
            level.Set(position, state.WithProperty("powered", target > 0), BlockUpdateFlags.Clients);

        UpdateFront(level, position, state);
    }

    // The one item frame in the block, hanging on the face toward the comparator's input, like vanilla's: none when
    // there are two.
    private static ItemFrame? ItemFrameAt(LevelBlockUpdates level, BlockFace facing, Vector position)
    {
        var frames = level.EntitiesIn((VectorD)position, (VectorD)(position + new Vector(1, 1, 1)))
            .OfType<ItemFrame>()
            .Where(frame => frame.Facing == facing)
            .Take(2)
            .ToList();

        return frames.Count == 1 ? frames[0] : null;
    }
}
