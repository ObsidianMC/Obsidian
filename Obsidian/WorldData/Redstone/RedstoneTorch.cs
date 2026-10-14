using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The redstone torch, standing or on a wall, like vanilla's <c>RedstoneTorchBlock</c> and
/// <c>RedstoneWallTorchBlock</c>: lit unless the block it's on is powered, two game ticks after that changes, and
/// burnt out for a while after toggling too often.
/// </summary>
internal sealed class RedstoneTorch(bool wall) : RedstoneComponent
{
    // Vanilla's torch delay, burn-out window, toggles that burn it out and the delay before it relights.
    private const int ToggleDelay = 2;
    private const int RecentToggleTime = 60;
    private const int MaxRecentToggles = 8;
    private const int RestartDelay = 160;

    // Vanilla's level event for a torch burning out: a fizz and smoke.
    private const int BurnOutEvent = 1502;

    // Vanilla's Direction.values().
    private static readonly BlockFace[] all =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    // A lit torch powers every side but the block it's on.
    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        IsLit(state) && direction != this.SupportDirection(state).Opposite() ? 15 : 0;

    // It strongly powers the block above it.
    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        direction == BlockFace.Down ? this.GetSignal(level, position, state, direction) : 0;

    // Vanilla's StandingAndWallBlockItem: the first of the nearest looking directions it survives in, but up, on the
    // floor for down, else on the wall it faces away from.
    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        var level = placement.Level;
        var wallState = WallPlacement(placement);

        foreach (var direction in placement.NearestLookingDirections)
        {
            if (direction == BlockFace.Up)
                continue;

            var state = direction == BlockFace.Down ? BlocksRegistry.GetDefault(BlockIds.RedstoneTorch) : wallState;
            if (state is not null && CanSurvive(level, placement.Position, state))
                return state;
        }

        return null;
    }

    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState) =>
        NotifyNeighbors(level, position, state);

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state) =>
        NotifyNeighbors(level, position, state);

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (IsLit(state) == this.HasNeighborSignal(level, position, state) && !level.WillTickThisTick(position, state))
            level.ScheduleTick(position, state, ToggleDelay);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor)
    {
        if (direction != this.SupportDirection(state) || CanSurvive(level, position, state))
            return state;

        return BlocksRegistry.Air;
    }

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var powered = this.HasNeighborSignal(level, position, state);
        var toggles = level.TorchToggles;
        toggles.Forget(level.GameTime - RecentToggleTime);

        if (IsLit(state))
        {
            if (!powered)
                return;

            level.Set(position, state.WithProperty("lit", false), BlockUpdateFlags.All);
            if (toggles.Count(position, level.GameTime, add: true) >= MaxRecentToggles)
            {
                level.LevelEvent(BurnOutEvent, position, 0);
                level.ScheduleTick(position, level.GetBlock(position), RestartDelay);
            }
        }
        else if (!powered && toggles.Count(position, level.GameTime, add: false) < MaxRecentToggles)
        {
            level.Set(position, state.WithProperty("lit", true), BlockUpdateFlags.All);
        }
    }

    private static bool IsLit(IBlock state) => state.GetProperty("lit") == "true";

    // Vanilla's notifyNeighbors: the neighbours of each of its neighbours.
    private static void NotifyNeighbors(LevelBlockUpdates level, Vector position, IBlock state)
    {
        foreach (var direction in all)
            level.UpdateNeighbors(position.Offset(direction), state);
    }

    // Vanilla's WallTorchBlock.getStateForPlacement: facing away from the first wall among the nearest looking
    // directions it survives on.
    private static IBlock? WallPlacement(RedstonePlacement placement)
    {
        var wall = BlocksRegistry.GetDefault(BlockIds.RedstoneWallTorch);
        foreach (var direction in placement.NearestLookingDirections)
        {
            if (direction.IsVertical())
                continue;

            var state = wall.WithProperty("facing", direction.Opposite().PropertyName());
            if (CanSurvive(placement.Level, placement.Position, state))
                return state;
        }

        return null;
    }

    // A standing torch needs the centre of the block below's top; a wall torch the full face of the block behind.
    private static bool CanSurvive(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (!state.Is(BlockIds.RedstoneWallTorch))
            return level.GetBlock(position + Vector.Down).IsTopCenterSturdy();

        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        return level.GetBlock(position.Offset(facing.Opposite())).IsFaceSturdy(facing);
    }

    // The side its support is on: below, or behind a wall torch.
    private BlockFace SupportDirection(IBlock state) =>
        wall ? FeatureHelpers.ParseFace(state.GetProperty("facing")).Opposite() : BlockFace.Down;

    // Vanilla's hasNeighborSignal: whether the block it's on powers it.
    private bool HasNeighborSignal(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var support = this.SupportDirection(state);
        return level.HasSignal(position.Offset(support), support);
    }
}

/// <summary>
/// A level's redstone torches' recent toggles, like vanilla's <c>RedstoneTorchBlock.RECENT_TOGGLES</c>: a torch that
/// turned off 8 times within 60 game ticks burns out.
/// </summary>
internal sealed class TorchToggles
{
    private readonly List<(Vector Position, long Time)> toggles = [];

    /// <summary>
    /// Forgets the toggles from before <paramref name="since"/>.
    /// </summary>
    public void Forget(long since)
    {
        var expired = 0;
        while (expired < this.toggles.Count && this.toggles[expired].Time < since)
            expired++;

        this.toggles.RemoveRange(0, expired);
    }

    /// <summary>
    /// The recent toggles of the torch at <paramref name="position"/>, counting one now first if
    /// <paramref name="add"/>.
    /// </summary>
    public int Count(Vector position, long now, bool add)
    {
        if (add)
            this.toggles.Add((position, now));

        return this.toggles.Count(toggle => toggle.Position == position);
    }
}
