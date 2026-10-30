using System.Threading;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A redstone torch's inversion, delay and burnout (vanilla <c>RedstoneTorchBlock</c>, checked against the 1.21.11 bytecode).
/// A torch is lit unless its support is powered. A neighbor change that leaves its state inconsistent schedules a check two ticks
/// later. Turning off eight times at one position within 60 game ticks burns the torch out for 160 ticks.
/// </summary>
internal sealed class RedstoneTorch(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior, IPlacementBehavior
{
    public const int InversionDelay = 2;
    public const int BurnoutDelay = 160;
    public const int BurnoutToggles = 8;
    public const int BurnoutWindow = 60;

    // Vanilla's level event for a torch burning out: a fizz and smoke.
    private const int BurnOutEvent = 1502;

    public const int StandingId = BlockIds.RedstoneTorch;
    public const int WallId = BlockIds.RedstoneWallTorch;

    private readonly List<(Vector Position, long Time)> toggles = [];
    private readonly Lock sync = new();

    /// <summary>The face a torch's power comes from: the block below a standing torch, or the wall a wall torch is attached to.</summary>
    public static BlockFace SupportFace(IBlock block) => block.BlockClass() == "RedstoneWallTorchBlock"
        ? FeatureHelpers.ParseFace(block.GetProperty("facing")).Opposite()
        : BlockFace.Down;

    public static bool IsTorch(IBlock block) => block.BlockClass() is "RedstoneTorchBlock" or "RedstoneWallTorchBlock";

    /// <summary>
    /// A neighbor changed. A torch whose state disagrees with its support (lit while powered, or unlit while not) schedules its check.
    /// The scheduler keeps one pending tick per block, so a torch already waiting on a burnout recovery keeps that tick.
    /// </summary>
    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld _)
    {
        this.CheckConsistency(change.Position, change.State);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// A torch placed onto a powered support (or beside a powered wire that makes the support powered) is checked as soon as it is
    /// placed; oracle-observed: a torch placed onto a powered stone turns off.
    /// </summary>
    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld _)
    {
        this.CheckConsistency(change.Position, change.New);
        return ValueTask.CompletedTask;
    }

    // A torch whose state disagrees with its support (lit while powered, or unlit while not) schedules its check.
    private void CheckConsistency(Vector position, IBlock state)
    {
        var lit = state.GetProperty("lit") == "true";
        if (lit == engine.ReceiverHasSignal(position, SupportFace(state)))
            level.ScheduleBlockTick(position, state, InversionDelay);
    }

    /// <summary>The scheduled check. Reads the block at its due time, so a block changed in between is not acted on.</summary>
    public async ValueTask TickAsync(Vector position)
    {
        var block = level.GetLoadedBlock(position);
        if (block is null || !IsTorch(block))
            return;

        var now = level.GameTime;
        var powered = engine.ReceiverHasSignal(position, SupportFace(block));
        if (block.GetProperty("lit") == "true")
        {
            if (!powered)
                return;

            var unlit = block.WithProperty("lit", "false");
            await level.SetBlockAsync(position, unlit);
            if (RecordToggle(position, now))
            {
                level.BroadcastLevelEvent(BurnOutEvent, position, 0);
                level.ScheduleBlockTick(position, unlit, BurnoutDelay);
            }
            return;
        }

        if (powered || IsBurntOut(position, now))
            return;

        await level.SetBlockAsync(position, block.WithProperty("lit", "true"));
    }

    // Vanilla records the toggle and reports whether the position now has enough toggles to burn out.
    private bool RecordToggle(Vector position, long now)
    {
        lock (this.sync)
        {
            this.Prune(now);
            this.toggles.Add((position, now));
            return this.CountAt(position) >= BurnoutToggles;
        }
    }

    private bool IsBurntOut(Vector position, long now)
    {
        lock (this.sync)
        {
            this.Prune(now);
            return this.CountAt(position) >= BurnoutToggles;
        }
    }

    // Vanilla drops toggles older than the window from the front of the list, at each torch tick.
    private void Prune(long now) => this.toggles.RemoveAll(toggle => now - toggle.Time > BurnoutWindow);

    private int CountAt(Vector position)
    {
        var count = 0;
        foreach (var toggle in this.toggles)
        {
            if (toggle.Position == position)
                count++;
        }

        return count;
    }
}
