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
    // Vanilla's torch delay, and the delay before a burnt-out torch relights.
    private const int ToggleDelay = 2;
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
        UpdateAround(level, position, state);

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state) =>
        UpdateAround(level, position, state);

    // A torch whose support's power disagrees with it toggles two game ticks later.
    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (IsLit(state) == this.IsSupportPowered(level, position, state) && !level.WillTickThisTick(position, state))
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

    // It goes out when its support is powered, burning out if that's too often, and relights when it isn't, unless
    // burnt out.
    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var powered = this.IsSupportPowered(level, position, state);
        var burnout = level.TorchBurnout;

        if (!IsLit(state))
        {
            if (!powered && !burnout.IsBurntOut(position, level.GameTime))
                level.Set(position, state.WithProperty("lit", true), BlockUpdateFlags.All);

            return;
        }

        if (!powered)
            return;

        level.Set(position, state.WithProperty("lit", false), BlockUpdateFlags.All);
        if (burnout.TurnedOff(position, level.GameTime))
        {
            level.LevelEvent(BurnOutEvent, position, 0);
            level.ScheduleTick(position, level.GetBlock(position), RestartDelay);
        }
    }

    private static bool IsLit(IBlock state) => state.GetProperty("lit") == "true";

    // The neighbours of each of its neighbours hear of it, like vanilla's torches update: it strongly powers the
    // block above, whose neighbours its power reaches.
    private static void UpdateAround(LevelBlockUpdates level, Vector position, IBlock state)
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

    // Whether the block it's on powers it.
    private bool IsSupportPowered(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var support = this.SupportDirection(state);
        return level.HasSignal(position.Offset(support), support);
    }
}

/// <summary>
/// The redstone torches of a level that turned off lately, for their burning out: like vanilla's, a torch that turned
/// off 8 times within 60 game ticks is burnt out until fewer did in the last 60.
/// </summary>
internal sealed class TorchBurnout
{
    // Vanilla's burn-out window and the turn-offs within it that burn a torch out.
    private const int Window = 60;
    private const int TurnOffsToBurnOut = 8;

    // In the order they happened, so that the expired ones are at the start.
    private readonly List<(Vector Position, long Time)> turnOffs = [];

    /// <summary>
    /// Counts the torch at <paramref name="position"/> turning off <paramref name="now"/>.
    /// </summary>
    /// <returns>Whether that burns it out.</returns>
    public bool TurnedOff(Vector position, long now)
    {
        this.Expire(now);
        this.turnOffs.Add((position, now));

        return this.CountAt(position) >= TurnOffsToBurnOut;
    }

    /// <summary>
    /// Whether the torch at <paramref name="position"/> is burnt out <paramref name="now"/>.
    /// </summary>
    public bool IsBurntOut(Vector position, long now)
    {
        this.Expire(now);
        return this.CountAt(position) >= TurnOffsToBurnOut;
    }

    private void Expire(long now)
    {
        var expired = 0;
        while (expired < this.turnOffs.Count && this.turnOffs[expired].Time < now - Window)
            expired++;

        this.turnOffs.RemoveRange(0, expired);
    }

    private int CountAt(Vector position) => this.turnOffs.Count(turnOff => turnOff.Position == position);
}
