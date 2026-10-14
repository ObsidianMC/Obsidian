using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

internal sealed partial class LevelBlockUpdates
{
    /// <inheritdoc/>
    public bool DestroyBlock(Vector position, bool dropItems, IEntity? breaker = null) =>
        level.DestroyBlock(position, dropItems, breaker);

    /// <inheritdoc/>
    public bool SpawnItem(VectorD position, ItemStack stack) => level.SpawnItem(position, stack);
}
