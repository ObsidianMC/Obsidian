using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The hopper's block, like vanilla's <c>HopperBlock</c>: it is disabled while a redstone signal reaches it, and shows
/// that to clients only. Its items move in <see cref="Hoppers"/>.
/// </summary>
internal sealed class RedstoneHopper(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior, IPlacementBehavior
{
    public const int BlockId = BlockIds.Hopper;

    public static void Register(IRedstoneLevel level, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var hopper = new RedstoneHopper(level, engine);
        neighbors.Register(BlockId, hopper);
        neighbors.RegisterPlaced(BlockId, hopper);
    }

    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.CheckPowerAsync(change.Position, change.New);

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.CheckPowerAsync(change.Position, change.State);

    // Vanilla's checkPoweredState: enabled exactly while no neighbour powers it.
    private async ValueTask CheckPowerAsync(Vector position, IBlock state)
    {
        var enabled = !engine.HasNeighborSignal(position);
        if ((state.GetProperty("enabled") == "true") != enabled)
            await level.WriteAsync(position, state.WithProperty("enabled", enabled), notifyNeighbors: false);
    }
}
