using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// An observer (vanilla <c>ObserverBlock</c>). It watches the block in front of its <c>facing</c> side. When that
/// block's state changes, it pulses: <c>powered</c> goes on for two game ticks, and the back side (opposite
/// <c>facing</c>) gives full power while it is on. It hears of the change through the shape update the change sends
/// it, so it sees changes written without neighbor updates (dust power, a repeater's output) and ignores neighbor
/// updates that change nothing (a container's items moving).
/// </summary>
internal sealed class RedstoneObserver(IRedstoneLevel level)
    : IRedstoneBehavior
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
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => observer.TickAsync(position)));
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        state.GetProperty("powered") == "true" && direction == Facing(state) ? RedstoneEngine.MaxSignal : 0;

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => this.GetSignal(state, position, direction, wiresGiveSignal);

    /// <summary>
    /// Command block inputs derive their state from neighbors before placement. For observers this sends a shape
    /// update from the watched side even when it is air. Player placement does not run that derivation.
    /// </summary>
    internal static void PrepareCommandPlacement(IBlockLevel level, Vector position, IBlock state)
    {
        if (!IsObserver(state))
            return;

        var facing = Facing(state);
        var neighborPosition = position.Offset(facing);
        ShapeUpdateBehaviors.ObserverBlock.UpdateShape(
            level.AsShapeLevel(), position, state, facing, neighborPosition, level.GetBlock(neighborPosition));
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

/// <summary>The observer's shape hook, shared by the fluid and block notification engines.</summary>
internal sealed class ObserverShapeBehavior : IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    public IBlock UpdateShape(IShapeLevel level, Vector position, IBlock state, BlockFace direction,
        Vector neighborPosition, IBlock neighbor)
    {
        if (state.GetProperty("powered") != "true"
            && direction == FeatureHelpers.ParseFace(state.GetProperty("facing")))
        {
            // ScheduleTick deduplicates the same change delivered by both engines.
            level.ScheduleTick(position, state, 2);
        }

        return state;
    }
}
