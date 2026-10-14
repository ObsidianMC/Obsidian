using Obsidian.API.BlockEntities;

namespace Obsidian.WorldData;

/// <summary>
/// Retires chunk-owned inventories before their final save, without removal drops or plugin callbacks.
/// </summary>
internal static class BlockEntityLifecycle
{
    internal static List<BaseContainer> Retire(Chunk chunk)
    {
        var inventories = new List<BaseContainer>();
        foreach (var entity in chunk.BlockEntities.Values.OfType<BlockEntity>())
        {
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
            entity.Host = null;
            entity.Owner = null;
        }

        return inventories;
    }
}
