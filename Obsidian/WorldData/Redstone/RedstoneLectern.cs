using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The lectern's block, like vanilla's <c>LecternBlock</c>: turning a page powers it for two game ticks, weakly to its
/// neighbours and strongly to the block under it.
/// </summary>
internal sealed class RedstoneLectern(IRedstoneLevel level) : IRedstoneBehavior
{
    public const int BlockId = BlockIds.Lectern;

    // How long a page turn powers the lectern (vanilla's LecternBlock.signalPageChange).
    public const int PageTurnPulse = 2;

    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone)
    {
        var lectern = new RedstoneLectern(level);
        redstone.Register(BlocksRegistry.GetDefault(BlockId).BlockClass(), lectern);
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => lectern.TickAsync(position)));
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        state.GetProperty("powered") == "true" ? RedstoneEngine.MaxSignal : 0;

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        direction == BlockFace.Up ? this.GetSignal(state, position, direction, wiresGiveSignal) : 0;

    /// <summary>The page turn's pulse ends.</summary>
    public async ValueTask TickAsync(Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is not null)
            await this.SetPoweredAsync(position, state, false);
    }

    /// <summary>
    /// Vanilla's <c>signalPageChange</c>: the lectern is powered for <see cref="PageTurnPulse"/> ticks.
    /// </summary>
    public static async ValueTask TurnPageAsync(IRedstoneLevel level, Vector position)
    {
        var state = level.GetLoadedBlock(position);
        if (state is null || !state.Is(BlockId))
            return;

        await new RedstoneLectern(level).SetPoweredAsync(position, state, true);

        var powered = level.GetLoadedBlock(position);
        if (powered is not null && powered.Is(BlockId))
            level.ScheduleBlockTick(position, powered, PageTurnPulse);
    }

    /// <summary>
    /// Vanilla's <c>changePowered</c>: the lectern is written and the block under it hears of the change.
    /// </summary>
    private async ValueTask SetPoweredAsync(Vector position, IBlock state, bool powered)
    {
        if (!state.Is(BlockId) || (state.GetProperty("powered") == "true") == powered)
            return;

        await level.WriteAsync(position, state.WithProperty("powered", powered), notifyNeighbors: true);
        await level.NotifyNeighborsAsync(position + new Vector(0, -1, 0));
    }
}
