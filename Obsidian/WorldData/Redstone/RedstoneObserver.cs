using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// An observer (vanilla <c>ObserverBlock</c>). It watches the block in front of its <c>facing</c> side. When that block changes, it
/// pulses: <c>powered</c> goes on for two game ticks, and the back side (opposite <c>facing</c>) gives full power while it is on.
/// Changes to the observer's own output are not watched, because it only reacts to its front block.
/// </summary>
internal sealed class RedstoneObserver(IRedstoneLevel level) : IRedstoneBehavior, INeighborChangedBehavior, IPlacementBehavior
{
    public const int BlockId = BlockIds.Observer;

    private const int PulseTicks = 2;

    public static bool IsObserver(IBlock block) => block.Is(BlockId);

    // Vanilla ObserverBlock.getStateForPlacement: faces the nearest looking direction, unpowered.
    public static IBlock DerivePlacement(IBlock state, BlockFace nearestLook) =>
        state.WithProperty("facing", nearestLook.ToString().ToLowerInvariant()).WithProperty("powered", "false");

    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors)
    {
        var observer = new RedstoneObserver(level);
        redstone.Register(BlocksRegistry.GetDefault(BlockId).BlockClass(), observer);
        neighbors.Register(BlockId, observer);
        neighbors.RegisterPlaced(BlockId, observer);
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => observer.TickAsync(position)));
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        state.GetProperty("powered") == "true" && direction == Facing(state) ? RedstoneEngine.MaxSignal : 0;

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => this.GetSignal(state, position, direction, wiresGiveSignal);

    // Vanilla onPlace: a newly placed observer schedules one pulse (oracle: on at the placement's next tick, off two ticks later).
    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.SchedulePulse(change.Position, change.New);

    // Vanilla startSignal: a change to the watched block schedules a pulse, and the tick turns the output on. A pulse already
    // scheduled is not added again, because scheduled ticks are deduplicated.
    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        if (change.Source == change.Position.Offset(Facing(change.State)))
            return this.SchedulePulse(change.Position, change.State);

        return ValueTask.CompletedTask;
    }

    private ValueTask SchedulePulse(Vector position, IBlock state)
    {
        if (state.GetProperty("powered") != "true")
            level.ScheduleBlockTick(position, state, PulseTicks);

        return ValueTask.CompletedTask;
    }

    // Vanilla ObserverBlock.tick: an on output goes off; an off output goes on and schedules its own turn-off.
    public async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is null || !IsObserver(state))
            return;

        if (state.GetProperty("powered") == "true")
        {
            await level.SetBlockAsync(position, state.WithProperty("powered", "false"));
            return;
        }

        var on = state.WithProperty("powered", "true");
        await level.SetBlockAsync(position, on);
        level.ScheduleBlockTick(position, on, PulseTicks);
    }

    private static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));
}
