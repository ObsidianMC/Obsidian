using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A repeater's delayed output and side lock (vanilla <c>RepeaterBlock</c>, checked against the R0 oracle scenarios).
/// The <c>facing</c> property names the input side; output goes to the opposite side after <c>2 × delay</c> game ticks.
/// A powered repeater or comparator feeding either side locks the repeater and prevents pending state changes from applying.
/// </summary>
internal sealed class RedstoneRepeater(IRedstoneLevel level, RedstoneEngine engine) : IRedstoneBehavior, INeighborChangedBehavior, IPlacementBehavior
{
    public const int BlockId = BlockIds.Repeater;

    public static bool IsRepeater(IBlock block) => block.Is(BlockId);

    public static IBlock DerivePlacement(IBlock state, float yaw) =>
        state.WithProperty("facing", PlacementFacing.FromYaw(yaw).Opposite().ToString().ToLowerInvariant());

    public static async ValueTask CycleDelayAsync(IRedstoneLevel level, Vector position, IBlock state)
    {
        var delay = ParseDelay(state);
        var next = delay % 4 + 1;
        await level.WriteAsync(position, state.WithProperty("delay", next.ToString()), notifyNeighbors: true);
    }

    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var repeater = new RedstoneRepeater(level, engine);
        redstone.Register(BlocksRegistry.GetDefault(BlockId).BlockClass(), repeater);
        neighbors.Register(BlockId, repeater);
        neighbors.RegisterPlaced(BlockId, repeater);
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => repeater.TickAsync(position)));
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        state.GetProperty("powered") == "true" && direction == InputFace(state) ? RedstoneEngine.MaxSignal : 0;

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => this.GetSignal(state, position, direction, wiresGiveSignal);

    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.UpdateAsync(change.Position, change.New);

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.UpdateAsync(change.Position, change.State);

    public async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is null || !IsRepeater(state))
            return;

        var locked = IsLocked(position, state);
        if ((state.GetProperty("locked") == "true") != locked)
        {
            state = state.WithProperty("locked", locked.ToString().ToLowerInvariant());
            await level.SetBlockAsync(position, state);
        }

        if (locked)
            return;

        // Vanilla RepeaterBlock.tick: the live input decides whether a powered repeater turns off. An unpowered one turns on whatever
        // its input is now, and schedules its turn-off when the input has already gone (a short pulse).
        var inputPowered = IsInputPowered(position, state);
        var powered = state.GetProperty("powered") == "true";
        if (powered && !inputPowered)
        {
            await this.SetOutputAsync(position, state, false);
        }
        else if (!powered)
        {
            var next = await this.SetOutputAsync(position, state, true);
            if (!inputPowered)
                level.ScheduleBlockTick(position, next, ParseDelay(state) * 2);
        }
    }

    // Commits the output and tells the block in front of it (vanilla updateNeighborsInFront).
    private async ValueTask<IBlock> SetOutputAsync(Vector position, IBlock state, bool powered)
    {
        var next = state.WithProperty("powered", powered.ToString().ToLowerInvariant());
        await level.SetBlockAsync(position, next);
        await BasicRedstoneComponents.NotifyOutputFrontAsync(level, position, InputFace(state).Opposite());
        return next;
    }

    private async ValueTask UpdateAsync(Vector position, IBlock state)
    {
        var locked = IsLocked(position, state);
        if ((state.GetProperty("locked") == "true") != locked)
        {
            state = state.WithProperty("locked", locked.ToString().ToLowerInvariant());
            await level.SetBlockAsync(position, state);
        }

        if (locked)
            return;

        var powered = IsInputPowered(position, state);
        if ((state.GetProperty("powered") == "true") != powered)
            level.ScheduleBlockTick(position, state, ParseDelay(state) * 2);
    }

    private bool IsInputPowered(Vector position, IBlock state) => engine.ReceiverHasSignal(position, InputFace(state));

    private bool IsLocked(Vector position, IBlock state)
    {
        var facing = InputFace(state);
        foreach (var side in new[] { facing.ClockWise(), facing.CounterClockWise() })
        {
            var neighbourPosition = position.Offset(side);
            var neighbour = level.GetLoadedBlock(neighbourPosition);
            if (neighbour is null || !IsLockSource(neighbour))
                continue;

            if (engine.GetSignal(neighbourPosition, side) > 0)
                return true;
        }

        return false;
    }

    private static bool IsLockSource(IBlock block) => block.Is(BlockId) || block.Is(BlockIds.Comparator);

    private static BlockFace InputFace(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    private static int ParseDelay(IBlock state) => int.TryParse(state.GetProperty("delay"), out var delay) ? Math.Clamp(delay, 1, 4) : 1;
}
