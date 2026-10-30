using Obsidian.Entities;
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
/// The analog output (0–15) is kept in the comparator's <see cref="ComparatorBlockEntity"/>, as vanilla does, so it is saved and
/// restored with the chunk and is never inferred from the powered property.
/// </remarks>
internal sealed class RedstoneComparator(IRedstoneLevel level, RedstoneEngine engine) : IRedstoneBehavior, INeighborChangedBehavior, IPlacementBehavior
{
    public const int BlockId = BlockIds.Comparator;

    private const int DelayTicks = 2;

    public static bool IsComparator(IBlock block) => block.Is(BlockId);

    public static IBlock DerivePlacement(IBlock state, float yaw) =>
        state.WithProperty("facing", PlacementFacing.FromYaw(yaw).Opposite().ToString().ToLowerInvariant());

    /// <summary>Toggles compare and subtract mode (vanilla use). The output is refreshed by a tick two ticks later.</summary>
    public static async ValueTask ToggleModeAsync(IRedstoneLevel level, Vector position, IBlock state, IPlayer player)
    {
        var mode = state.GetProperty("mode") == "subtract" ? "compare" : "subtract";
        await level.WriteAsync(position, state.WithProperty("mode", mode), notifyNeighbors: false);
        level.PlaySound(position, "minecraft:block.comparator.click", 0.3f, mode == "subtract" ? 0.55f : 0.5f, player);
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
        direction == Facing(state) ? this.Output(position) : 0;

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => this.GetSignal(state, position, direction, wiresGiveSignal);

    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.ScheduleIfChanged(change.Position, change.New);

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.ScheduleIfChanged(change.Position, change.State);

    /// <summary>A scheduled refresh: recompute the output and, when it changed, commit it and notify the neighbours.</summary>
    public async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is null || !IsComparator(state) || level.GetBlockEntityAt(position) is not ComparatorBlockEntity entity)
            return;

        var target = this.Calculate(position, state);
        if (target == entity.OutputSignal)
            return;

        entity.OutputSignal = target;
        entity.SetChanged();
        var powered = (target > 0).ToString().ToLowerInvariant();
        if (state.GetProperty("powered") != powered)
            await level.SetBlockAsync(position, state.WithProperty("powered", powered));

        // Vanilla ComparatorBlock.tick: an output change goes to the block in front of the comparator.
        await BasicRedstoneComponents.NotifyOutputFrontAsync(level, position, Facing(state).Opposite());
    }

    private ValueTask ScheduleIfChanged(Vector position, IBlock state)
    {
        if (this.Calculate(position, state) != this.Output(position))
        {
            level.ScheduleBlockTick(position, state, DelayTicks, ShouldPrioritize(position, state)
                ? Obsidian.API.Blocks.TickPriority.High
                : Obsidian.API.Blocks.TickPriority.Normal);
        }

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

    /// <summary>
    /// Vanilla <c>ComparatorBlock.getInputSignal</c> for the rear: the block behind's analog output when it has one. Otherwise, when
    /// that block is a conductor short of full power, the block beyond it or an item frame on its face gives the analog output.
    /// </summary>
    private int RearInput(Vector position, BlockFace facing)
    {
        var signal = engine.GetSignal(position, facing);
        var input = level.GetLoadedBlock(position);
        if (input is null)
            return signal;

        if (level.HasAnalogOutput(input))
            return level.GetAnalogOutput(position, input, facing.Opposite());

        if (signal >= RedstoneEngine.MaxSignal || !input.IsRedstoneConductor())
            return signal;

        var further = position.Offset(facing);
        var analog = int.MinValue;
        if (ItemFrameAt(further, facing) is { } frame)
            analog = frame.Item is { IsAir: false } ? frame.Rotation % 8 + 1 : 0;

        if (level.GetLoadedBlock(further) is { } beyond && level.HasAnalogOutput(beyond))
            analog = Math.Max(analog, level.GetAnalogOutput(further, beyond, facing.Opposite()));

        return analog != int.MinValue ? analog : signal;
    }

    /// <summary>
    /// Vanilla <c>ComparatorBlock.getAlternateSignal</c> for a side: the redstone signal the neighbour gives, with no analog
    /// output and no look-through.
    /// </summary>
    private int SideInput(Vector position, BlockFace direction) => engine.GetSignal(position, direction);

    // The one item frame on the face toward the comparator's input, like vanilla's: none when there are two.
    private ItemFrame? ItemFrameAt(Vector position, BlockFace facing)
    {
        var frames = level.EntitiesIn((VectorD)position, (VectorD)(position + new Vector(1, 1, 1)))
            .OfType<ItemFrame>()
            .Where(frame => frame.Facing == facing)
            .Take(2)
            .ToList();

        return frames.Count == 1 ? frames[0] : null;
    }

    private int Calculate(Vector position, IBlock state)
    {
        var facing = Facing(state);
        var rear = this.RearInput(position.Offset(facing), facing);

        var side = 0;
        foreach (var sideFace in new[] { facing.ClockWise(), facing.CounterClockWise() })
            side = Math.Max(side, this.SideInput(position.Offset(sideFace), sideFace));

        if (state.GetProperty("mode") == "subtract")
            return Math.Max(rear - side, 0);

        return rear < side ? 0 : rear;
    }

    /// <summary>The comparator's stored output (vanilla <c>OutputSignal</c>); 0 when its block entity isn't resident.</summary>
    private int Output(Vector position) =>
        level.GetBlockEntityAt(position) is ComparatorBlockEntity entity ? entity.OutputSignal : 0;

    private static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));
}
