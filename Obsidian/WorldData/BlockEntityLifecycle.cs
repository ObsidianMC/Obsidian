using Obsidian.API.BlockEntities;

namespace Obsidian.WorldData;

/// <summary>
/// Retires chunk-owned block entities before their chunk's final save, without removal drops or
/// <see cref="BlockEntity.OnRemoved"/>: the save tells them their chunk unloaded once it let go of its locks.
/// </summary>
internal static class BlockEntityLifecycle
{
    /// <summary>
    /// Marks the live block entities of an unloading <paramref name="chunk"/> removed, retiring their containers'
    /// inventories, under the save's locks.
    /// </summary>
    /// <param name="chunk">The chunk being unloaded.</param>
    /// <param name="inventories">Receives the retired inventories, whose windows the caller closes.</param>
    /// <param name="unloaded">
    /// Receives the retired block entities, which stay in their level until the caller tells them their chunk unloaded
    /// and then calls <see cref="Detach"/>.
    /// </param>
    internal static void Retire(Chunk chunk, List<BaseContainer> inventories, List<BlockEntity> unloaded)
    {
        foreach (var entity in chunk.BlockEntities.Values.OfType<BlockEntity>())
        {
            // A close handler may flush an already retired region; it must not close the same window recursively.
            if (entity.IsRemoved)
                continue;

            if (entity is ContainerBlockEntity container)
            {
                using (container.Inventory.Lock())
                {
                    container.Inventory.TryRetire();
                    entity.IsRemoved = true;
                }

                inventories.Add(container.Inventory);
            }

            entity.IsRemoved = true;
            unloaded.Add(entity);
        }
    }

    /// <summary>
    /// Lets go of a retired block entity's level and plugin, once it heard its chunk unloaded.
    /// </summary>
    internal static void Detach(BlockEntity entity)
    {
        entity.Host = null;
        entity.Owner = null;
    }
}
