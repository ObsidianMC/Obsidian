using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A redstone lamp (vanilla <c>RedstoneLampBlock</c>, checked against the 1.21.11 bytecode). It lights at once when it gets power,
/// and goes out four ticks after it loses power if it is still unpowered then. Both changes write without notifying neighbors.
/// </summary>
internal sealed class EngineRedstoneLamp(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior
{
    public const int BlockId = BlockIds.RedstoneLamp;
    public const int TurnOffDelay = 4;

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld _)
    {
        var lit = change.State.GetProperty("lit") == "true";
        var powered = engine.HasNeighborSignal(change.Position);
        if (lit == powered)
            return ValueTask.CompletedTask;

        if (lit)
        {
            level.ScheduleBlockTick(change.Position, change.State, TurnOffDelay);
            return ValueTask.CompletedTask;
        }

        return level.WriteAsync(change.Position, change.State.WithProperty("lit", "true"), notifyNeighbors: false);
    }

    /// <summary>The scheduled turn-off. Turns the lamp out only if it is still lit and unpowered when the tick comes due.</summary>
    public async ValueTask TickAsync(Vector position)
    {
        var block = level.GetLoadedBlock(position);
        if (block is null || !block.Is(BlockId))
            return;

        if (block.GetProperty("lit") == "true" && !engine.HasNeighborSignal(position))
            await level.WriteAsync(position, block.WithProperty("lit", "false"), notifyNeighbors: false);
    }
}
