using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>Transfers the hand item to an unpublished block entity in the block mutation's critical section.</summary>
internal sealed class BlockPlacement(IPlayer player, int slot, ItemStack item)
{
    internal IDisposable LockHand() => player.Inventory.Lock();

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
            BlockEntityComponents.Apply(entity, new ItemStack(item, 1));
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
            player.Inventory.RemoveItem(slot, 1);
    }

    internal sealed class Prepared(BlockEntity? entity, IDisposable? call) : IDisposable
    {
        internal BlockEntity? Entity { get; } = entity;

        public void Dispose() => call?.Dispose();
    }
}
