using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Plugins;

namespace Obsidian.WorldData;

/// <summary>
/// Transfers the hand item to an unpublished block entity in the block mutation's critical section. Without a player
/// (<see cref="ForDispenser"/>), the item is one a dispenser holds locked and spends itself: no hand is locked,
/// checked or used up here.
/// </summary>
internal sealed class BlockPlacement(IPlayer? player, int slot, ItemStack item)
{
    /// <summary>A dispenser placing <paramref name="item"/>, which it spends itself once the block is placed.</summary>
    internal static BlockPlacement ForDispenser(ItemStack item) => new(null, 0, item);

    /// <summary>
    /// What else the placement changes in the same critical section, once the block is set and the item used: a
    /// door's upper half, a bed's head (vanilla's <c>setPlacedBy</c>). Nothing can change the world in between.
    /// </summary>
    internal Action? Placed { get; init; }

    // Inventory hooks can await world writes while owning this inventory. Never wait for it under tick.
    internal bool TryLockHand(out IDisposable? held)
    {
        held = null;
        return player is null || player.Inventory.TryLock(out held);
    }

    // Test rendezvous after a busy attempt has unwound its tick lock, before the bounded wait.
    internal Action? BeforeHandWait { get; init; }

    // One game tick is enough to bridge a short item hook without leaving a click pending indefinitely.
    // Release before returning: the caller must try again under tick, never carry this hand lock into tick.
    internal async ValueTask<bool> WaitForHandAsync()
    {
        this.BeforeHandWait?.Invoke();
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            using (await BaseContainer.LockAllAsync(player.Inventory.LockParts, timeout.Token))
            {
            }

            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
    }

    internal Prepared? Prepare(IBlock block, Vector position, ILogger logger)
    {
        var inHand = player is null || ReferenceEquals(player.Inventory.GetItem(slot), item);
        if (!inHand || item.Count <= 0)
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
        if (player is not null && player.GameMode != GameMode.Creative)
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
