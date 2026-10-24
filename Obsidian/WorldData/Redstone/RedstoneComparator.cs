using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A comparator (vanilla <c>ComparatorBlock</c>). The rear input is the side <c>facing</c> names; the two side inputs are the
/// perpendicular neighbours; the output goes to the opposite side. Compare mode passes the rear value when it is at least the side
/// value, and subtract mode outputs the rear value minus the side value. A change in the output takes effect two game ticks later.
/// </summary>
/// <remarks>
/// The analog output (0–15) is kept in memory per comparator, where vanilla keeps it in the block entity. It is recomputed from the
/// inputs whenever it is refreshed, so the only thing lost on a restart is the value between a change and its scheduled refresh.
/// Until a refresh runs after a restart, an output reads as 15 when powered and 0 otherwise.
/// </remarks>
internal sealed class RedstoneComparator(IRedstoneLevel level, RedstoneEngine engine) : IRedstoneBehavior, INeighborChangedBehavior, IPlacementBehavior
{
    public const int BlockId = BlockIds.Comparator;

    private const int DelayTicks = 2;

    private readonly Dictionary<Vector, int> outputs = [];

    public static bool IsComparator(IBlock block) => block.Is(BlockId);

    public static IBlock DerivePlacement(IBlock state, float yaw) =>
        state.WithProperty("facing", PlacementFacing.FromYaw(yaw).Opposite().ToString().ToLowerInvariant());

    /// <summary>Toggles compare and subtract mode (vanilla use). The output is refreshed by a tick two ticks later.</summary>
    public static async ValueTask ToggleModeAsync(IRedstoneLevel level, Vector position, IBlock state)
    {
        var mode = state.GetProperty("mode") == "subtract" ? "compare" : "subtract";
        await level.WriteAsync(position, state.WithProperty("mode", mode), notifyNeighbors: false);
        level.ScheduleBlockTick(position, state, DelayTicks);
    }

    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var comparator = new RedstoneComparator(level, engine);
        redstone.Register(BlocksRegistry.GetDefault(BlockId).BlockClass(), comparator);
        neighbors.Register(BlockId, comparator);
        neighbors.RegisterPlaced(BlockId, comparator);
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => comparator.TickAsync(position)));
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        direction == Facing(state) ? this.Output(position, state) : 0;

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => this.GetSignal(state, position, direction, wiresGiveSignal);

    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.ScheduleIfChanged(change.Position, change.New);

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.ScheduleIfChanged(change.Position, change.State);

    /// <summary>A scheduled refresh: recompute the output and, when it changed, commit it and notify the neighbours.</summary>
    public async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is null || !IsComparator(state))
        {
            this.outputs.Remove(position);
            return;
        }

        var target = this.Calculate(position, state);
        if (target == this.Output(position, state))
            return;

        this.outputs[position] = target;
        var powered = (target > 0).ToString().ToLowerInvariant();
        if (state.GetProperty("powered") != powered)
            await level.SetBlockAsync(position, state.WithProperty("powered", powered));
        else
            await level.NotifyNeighborsAsync(position);
    }

    private ValueTask ScheduleIfChanged(Vector position, IBlock state)
    {
        if (this.Calculate(position, state) != this.Output(position, state))
            level.ScheduleBlockTick(position, state, DelayTicks, ShouldPrioritize(position, state)
                ? Obsidian.API.Blocks.TickPriority.High
                : Obsidian.API.Blocks.TickPriority.Normal);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Vanilla <c>DiodeBlock.shouldPrioritize</c>: a diode whose output faces another diode turned another way runs first, so
    /// the diode it feeds reads its new output in the same tick.
    /// </summary>
    private bool ShouldPrioritize(Vector position, IBlock state)
    {
        var facing = Facing(state);
        var front = level.GetLoadedBlock(position.Offset(facing));
        return front is not null && (IsComparator(front) || front.Is(RedstoneRepeater.BlockId))
            && Facing(front) != facing;
    }

    /// <summary>Vanilla's <c>getInputSignal</c>: a container's analog output when it has one, else the signal the neighbour gives.</summary>
    private int InputSignal(Vector position, BlockFace direction)
    {
        var block = level.GetLoadedBlock(position);
        if (block is not null && level.HasAnalogOutput(block))
            return level.GetAnalogOutput(position, block, direction);

        return engine.GetSignal(position, direction);
    }

    private int Calculate(Vector position, IBlock state)
    {
        var facing = Facing(state);
        var rear = this.InputSignal(position.Offset(facing), facing);

        var side = 0;
        foreach (var sideFace in new[] { facing.ClockWise(), facing.CounterClockWise() })
            side = Math.Max(side, this.InputSignal(position.Offset(sideFace), sideFace));

        if (state.GetProperty("mode") == "subtract")
            return Math.Max(rear - side, 0);

        return rear < side ? 0 : rear;
    }

    private int Output(Vector position, IBlock state) =>
        this.outputs.TryGetValue(position, out var output) ? output : state.GetProperty("powered") == "true" ? RedstoneEngine.MaxSignal : 0;

    private static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));
}
