using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// TNT (vanilla <c>TntBlock</c>): a rising redstone signal at it, or a placement next to power, primes it. The block goes at once and
/// the lit TNT spawns on the level's entity path, as entities do. Nothing happens while the <c>tnt_explodes</c> rule is off.
/// </summary>
internal sealed class TntIgnition(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior, IPlacementBehavior
{
    public const int BlockId = BlockIds.Tnt;

    public static void Register(IRedstoneLevel level, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var tnt = new TntIgnition(level, engine);
        neighbors.Register(BlockId, tnt);
        neighbors.RegisterPlaced(BlockId, tnt);
    }

    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.PrimeIfPowered(change.Position, world);

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world) => this.PrimeIfPowered(change.Position, world);

    private async ValueTask PrimeIfPowered(Vector position, IBlockChangeWorld world)
    {
        if (!engine.HasNeighborSignal(position) || !level.TntExplodes)
            return;

        await world.SetBlockAsync(position, BlocksRegistry.Air);
        level.PrimeTnt(position);
    }
}
