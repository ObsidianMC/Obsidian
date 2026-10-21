using Obsidian.API.Blocks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Vanilla's <c>TntBlock</c> redstone: placed onto, or next to, a signal, the block primes and removes itself.
/// </summary>
internal sealed class TntComponent : RedstoneComponent
{
    // Vanilla's onPlace: a block that was already TNT is left alone.
    public override void OnPlaced(LevelBlockUpdates level, Vector position, IBlock state, IBlock oldState)
    {
        if (oldState.RegistryId == state.RegistryId)
            return;

        if (level.HasNeighborSignal(position))
            Ignite(level, position);
    }

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (level.HasNeighborSignal(position))
            Ignite(level, position);
    }

    // Vanilla's TntBlock.neighborChanged: prime with a null igniter, then remove the block.
    private static void Ignite(LevelBlockUpdates level, Vector position)
    {
        if (level.Level is AbstractLevel concrete && concrete.PrimeTnt(position))
            level.Set(position, BlocksRegistry.Air, BlockUpdateFlags.All);
    }
}
