using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>Transfers the hand item to an unpublished block entity in the block mutation's critical section.</summary>
internal sealed class BlockPlacement(IPlayer player, int slot, ItemStack item)
{
    /// <summary>
    /// What else the placement changes in the same critical section, once the block is set and the item used: a
    /// door's upper half, a bed's head (vanilla's <c>setPlacedBy</c>). Nothing can change the world in between.
    /// </summary>
    internal Action? Placed { get; init; }

    // Inventory hooks can await world writes while owning this inventory. Never wait for it under tick.
    internal bool TryLockHand(out IDisposable? held) => player.Inventory.TryLock(out held);

    internal Prepared? Prepare(IBlock block, Vector position, ILogger logger)
    {
        if (!ReferenceEquals(player.Inventory.GetItem(slot), item) || item.Count <= 0)
            return null;

        var type = block.HasBlockEntity() ? block.BlockEntityType() : null;
        if (type is null)
            return new Prepared(null, null);

        var entity = BlockEntityNbt.Create(type, position, logger: logger);
        var plugin = entity.Owner as PluginContainer;
        var call = plugin?.TryEnter();
        if (plugin is not null && call is null
            || entity is DataBlockEntity && !type.StartsWith("minecraft:", StringComparison.Ordinal)
            || entity is ContainerBlockEntity { Inventory.IsRetired: true })
        {
            call?.Dispose();
            return null;
        }

        try
        {
            BlockEntityComponents.Apply(entity, new ItemStack(item, 1), logger);
            return new Prepared(entity, call);
        }
        catch
        {
            call?.Dispose();
            throw;
        }
    }

    internal void Consume()
    {
        if (player.GameMode != GameMode.Creative)
        {
            if (item.Holder.Id == ItemIds.PowderSnowBucket)
                player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Bucket, 1));
            else
                player.Inventory.RemoveItem(slot, 1);
        }
    }

    internal sealed class Prepared(BlockEntity? entity, IDisposable? call) : IDisposable
    {
        internal BlockEntity? Entity { get; } = entity;

        public void Dispose() => call?.Dispose();
    }
}
