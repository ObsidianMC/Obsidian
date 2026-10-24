using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;
using Obsidian.WorldData;

namespace Obsidian.Events;

// The beacon's, lectern's and crafter's windows, which show their block entities' state.
public partial class MainEventHandler
{
    /// <summary>
    /// A player using a lectern, like vanilla's <c>LecternBlock.useItemOn</c>: one with a book opens it; an empty one
    /// takes the written or writable book the player holds.
    /// </summary>
    /// <returns>A placeholder for the lectern's own container to open, or null when it doesn't open.</returns>
    private static async ValueTask<BaseContainer?> LecternUseAsync(IPlayer player, ItemStack? item, InteractionHand hand, Vector position)
    {
        if (player.Level is not AbstractLevel level || await level.GetOrCreateBlockEntityAsync(position) is not LecternBlockEntity lectern)
            return null;

        if (lectern.Lectern.Book is not null)
            return new LecternContainer();

        if (item is not { Count: > 0 } || !TagsRegistry.Item.LecternBooks.Entries.Contains(item.Holder.Id))
            return null;

        lectern.PlaceBook(item);

        // Like vanilla's consumeAndReturn, creative players keep theirs.
        if (player.GameMode != GameMode.Creative)
        {
            var slot = hand == InteractionHand.OffHand ? OffhandSlot : player.CurrentHeldItemSlot;
            player.Inventory.RemoveItem(slot, 1);
            await player.QueuePacketAsync(new ContainerSetSlotPacket
            {
                ContainerId = 0,
                Slot = (short)slot,
                SlotData = player.Inventory.GetItem(slot)
            });
        }

        return null;
    }

    /// <summary>
    /// The data slots of the player's open beacon, lectern or crafter: a beacon's levels and powers (effect ids plus
    /// one, 0 for none), a lectern's page, a crafter's slot states (1 for disabled) and whether it's powered.
    /// </summary>
    private static IEnumerable<IWindowProperty> RedstoneMenuProperties(IPlayer player)
    {
        switch (player.OpenedContainer)
        {
            case BeaconContainer beacon when BeaconAt(player, beacon.BlockPosition) is BeaconBlockEntity entity:
                yield return new BeaconWindowProperty(BeaconProperty.PowerLevel, (short)entity.Levels);
                yield return new BeaconWindowProperty(BeaconProperty.FirstPotionEffect, (short)(entity.Primary + 1 ?? 0));
                yield return new BeaconWindowProperty(BeaconProperty.SecondPotionEffect, (short)(entity.Secondary + 1 ?? 0));
                break;
            case LecternContainer lectern:
                yield return new LecternWindowProperty((short)lectern.Page);
                break;
            case CrafterContainer crafter:
                for (var slot = 0; slot < CrafterContainer.GridSize; slot++)
                    yield return new ContainerDataProperty((short)slot, (short)(crafter.IsSlotDisabled(slot) ? 1 : 0));

                yield return new ContainerDataProperty(CrafterContainer.GridSize, (short)(crafter.Triggered ? 1 : 0));
                break;
        }
    }

    private static BeaconBlockEntity? BeaconAt(IPlayer player, Vector position) =>
        player.Level is AbstractLevel level ? level.BlockUpdates.GetBlockEntity(position) as BeaconBlockEntity : null;

    /// <summary>
    /// What a crafter's window shows in its result slot: what its grid crafts, its disabled slots counting as empty.
    /// </summary>
    internal static ItemStack? CrafterResult(IPlayer player, CrafterContainer crafter)
    {
        var grid = Enumerable.Range(0, CrafterContainer.GridSize)
            .Select(slot => crafter.IsSlotDisabled(slot) ? null : crafter.GetItem(slot))
            .ToArray();

        return PlayerRecipes(player).EvaluateCrafting(grid, 3)?.Output;
    }

    /// <summary>
    /// Sets the powers of the player's open beacon, like vanilla's <c>BeaconMenu.updateEffects</c>: it takes the
    /// payment item, and only powers some pyramid level gives.
    /// </summary>
    internal static async ValueTask SetBeaconAsync(IPlayer player, int? primary, int? secondary)
    {
        if (player.OpenedContainer is not BeaconContainer beacon || !player.Alive)
            return;

        if (primary is int first && !BeaconBlockEntity.IsValidEffect(first)
            || secondary is int second && !BeaconBlockEntity.IsValidEffect(second))
        {
            return;
        }

        using (await BaseContainer.LockWindowAsync(beacon, player.Inventory))
        {
            if (!ReferenceEquals(player.OpenedContainer, beacon)
                || beacon.GetItem(BeaconContainer.PaymentSlot) is not { Count: > 0 }
                || BeaconAt(player, beacon.BlockPosition) is not BeaconBlockEntity entity)
            {
                return;
            }

            entity.Primary = primary;
            entity.Secondary = secondary;
            entity.SetChanged();
            beacon.RemoveItem(BeaconContainer.PaymentSlot, 1);

            if (entity.Levels > 0 && player.Level is AbstractLevel level)
                level.BlockUpdates.PlaySound(beacon.BlockPosition, "minecraft:block.beacon.power_select");
        }

        await RefreshViewersAsync(beacon);
    }

    /// <summary>
    /// Enables or disables a slot of the player's open crafter, like vanilla's <c>CrafterMenu.setSlotState</c>: only
    /// an empty slot changes.
    /// </summary>
    internal static async ValueTask SetCrafterSlotStateAsync(IPlayer player, int containerId, int slot, bool enabled)
    {
        if (player.OpenedContainer is not CrafterContainer crafter
            || containerId != player.CurrentContainerId
            || slot is < 0 or >= CrafterContainer.GridSize
            || player.GameMode == GameMode.Spectator)
        {
            return;
        }

        using (await BaseContainer.LockWindowAsync(crafter, player.Inventory))
        {
            if (!ReferenceEquals(player.OpenedContainer, crafter) || !crafter.GetItem(slot).IsNullOrAir())
                return;

            crafter.SetSlotDisabled(slot, !enabled);
        }

        if (player.Level is AbstractLevel level && level.BlockUpdates.GetBlockEntity(crafter.BlockPosition) is CrafterBlockEntity entity)
        {
            entity.SetChanged();
            lock (level.Fluids.TickLock)
                level.BlockUpdates.UpdateAnalogOutput(crafter.BlockPosition);
        }

        await RefreshViewersAsync(crafter);
    }

    /// <summary>
    /// Handles a button of the player's open lectern, like vanilla's <c>LecternMenu.clickMenuButton</c>: 1 and 2 turn
    /// back and on a page, 3 takes the book, and 100 on opens the book at that page.
    /// </summary>
    internal static async ValueTask ClickLecternButtonAsync(IPlayer player, int containerId, int button)
    {
        if (player.OpenedContainer is not LecternContainer lectern
            || containerId != player.CurrentContainerId
            || !player.Alive
            || player.Level is not AbstractLevel level
            || level.BlockUpdates.GetBlockEntity(lectern.BlockPosition) is not LecternBlockEntity entity
            || !ReferenceEquals(entity.Lectern, lectern))
        {
            return;
        }

        switch (button)
        {
            case 1:
                await entity.SetPageAsync(lectern.Page - 1);
                break;
            case 2:
                await entity.SetPageAsync(lectern.Page + 1);
                break;
            case 3:
                // Like vanilla's mayBuild: adventure and spectator players can't take it.
                if (player.GameMode is GameMode.Adventure or GameMode.Spectator)
                    return;

                if (entity.TakeBook() is ItemStack book)
                {
                    using (await BaseContainer.LockWindowAsync(lectern, player.Inventory))
                    {
                        StoreCraftingItem(player.Inventory, book, 9, 45);
                        SpawnThrownItem(player, book.Count > 0 ? book : null);
                    }

                    await player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()));
                }

                await player.CloseContainerAsync(null);
                return;
            case >= 100:
                await entity.SetPageAsync(button - 100);
                break;
            default:
                return;
        }

        await RefreshViewersAsync(lectern);
    }
}
