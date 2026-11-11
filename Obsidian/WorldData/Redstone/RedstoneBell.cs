using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Bells (vanilla <c>BellBlock</c>): their <c>powered</c> state follows their neighbours' signal, and a rising edge
/// rings them toward their facing, as a hit does.
/// </summary>
internal sealed class RedstoneBell(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior
{
    /// <summary>Vanilla's <c>BellBlock.EVENT_BELL_RING</c>, the block event that swings the bell on clients.</summary>
    internal const int RingEvent = 1;

    /// <summary>The sound a ringing bell plays, at vanilla's volume.</summary>
    internal const string RingSound = "minecraft:block.bell.use";

    internal const float RingVolume = 2;

    public static void Register(IRedstoneLevel level, NeighborBehaviors neighbors, RedstoneEngine engine) =>
        neighbors.Register(BlockIds.Bell, new RedstoneBell(level, engine));

    // Vanilla BellBlock.neighborChanged: the bell rings before it records its new power.
    public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        var powered = engine.HasNeighborSignal(change.Position);
        if ((change.State.GetProperty("powered") == "true") == powered)
            return;

        if (powered)
            this.Ring(change.Position, change.State);

        await world.SetBlockAsync(change.Position, change.State.WithProperty("powered", powered));
    }

    // Vanilla attemptToRing without an entity or a hit face: the bell swings toward its facing. A bell without its
    // block entity stays silent.
    private void Ring(Vector position, IBlock bell)
    {
        if (level.GetBlockEntityAt(position) is null)
            return;

        // The block event carries the face as vanilla's 3D data value, which BlockFace's order matches.
        var facing = FeatureHelpers.ParseFace(bell.GetProperty("facing"));
        level.QueueBlockEvent(position, bell.RegistryId, RingEvent, (int)facing, () => ValueTask.FromResult(true));
        level.PlaySound(position, RingSound, RingVolume);
        level.EmitGameEvent(MobGameEvent.BlockChange, position, null);
    }
}
