using Microsoft.Extensions.Logging;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Runtime.InteropServices;

namespace Obsidian.Events;
public partial class MainEventHandler
{
    private const int OutsideInventory = -999;

    [EventPriority(Priority = Priority.Internal)]
    public async ValueTask OnInventoryClick(ContainerClickEventArgs args)
    {
        if (args.IsCancelled)
            return;

        switch (args.ClickType)
        {
            case ClickType.Pickup:
                HandlePickup(args);
                break;
            case ClickType.Swap:
                HandleSwap(args);
                break;
            case ClickType.Throw:
                HandleThrow(args);
                break;
            case ClickType.QuickCraft:
                HandleQuickCraft(args);
                break;
            case ClickType.PickupAll:
                HandlePickupAll(args);
                break;

            case ClickType.QuickMove:
            case ClickType.Clone:
            default:
                break;
        }

        await this.HandleCraftingAsync(args);
    }

    private async ValueTask HandleCraftingAsync(ContainerClickEventArgs args)
    {
        var container = args.Container;
        var player = args.Player;

        if (container is not CraftingTable table)
            return;

        var recipe = RecipesRegistry.FindRecipe(table);

        if (recipe is null)
        {
            if (container[9] != null)
                container.RemoveItem(9);

            await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
            {
                Slot = 0,
                ContainerId = player.CurrentContainerId,
                SlotData = null
            });

            logger.LogTrace("No recipe found: {table}", table);
            return;
        }

        logger.LogTrace("Found Recipe: {recipe}", recipe.Identifier);

        var result = recipe.Result.First();
        table.SetResult(result);

        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        {
            Slot = 0,
            ContainerId = player.CurrentContainerId,
            SlotData = result
        });
    }

    private static void HandlePickupAll(ContainerClickEventArgs args)
    {
        var container = args.Container;
        var player = args.Player;
        var carriedItem = args.Item;

        if (carriedItem == null || carriedItem.Count >= carriedItem.MaxStackSize)
            return;

        int amountNeeded = carriedItem.MaxStackSize - carriedItem.Count;
        if (amountNeeded == 0)
            return;

        for (int i = 0; i < container.Size; i++)
        {
            ItemStack? item = container[i];
            if (item is null || item != carriedItem)
                continue;

            int amountTaken = Math.Min(item.Count, amountNeeded);
            item -= amountTaken;

            if (item.Count == 0)
                container.RemoveItem(i);

            carriedItem += amountTaken;
            amountNeeded -= amountTaken;

            if (amountNeeded == 0)
                break;
        }

        //Try the player inventory
        if (amountNeeded > 0 && !args.IsPlayerInventory)
        {
            for (int i = 0; i < player.Inventory.Size; i++)
            {
                ItemStack? item = player.Inventory[i];
                if (item is null || item != carriedItem)
                    continue;

                int amountTaken = Math.Min(item.Count, amountNeeded);
                item -= amountTaken;

                if (item.Count == 0)
                    player.Inventory.RemoveItem(i);

                carriedItem += amountTaken;
                amountNeeded -= amountTaken;

                if (amountNeeded == 0)
                    break;
            }
        }
    }

    private static void HandleQuickCraft(ContainerClickEventArgs args)
    {
        var container = args.Container;
        var player = args.Player;
        var clickedSlot = args.ClickedSlot;
        var state = (DraggingState)args.Button;

        if (!player.IsDragging && player.CarriedItem != null)
        {
            if (player.DraggedSlots.Count == 0 || state != DraggingState.EndLeft)
                return;

            int totalItems = player.CarriedItem.Count;
            int perSlot = totalItems / player.DraggedSlots.Count;
            //int remainder = totalItems % player.DraggedSlots.Count;

            var span = CollectionsMarshal.AsSpan(player.DraggedSlots);
            for (var i = 0; i < player.DraggedSlots.Count; i++)
            {
                var slotIndex = span[i];
                var item = container.GetItem(slotIndex);

                var amount = Math.Min(perSlot, player.CarriedItem.MaxStackSize - (item?.Count ?? 0));

                if (amount > 0)
                {
                    container.SetItem(slotIndex, item ?? new(player.CarriedItem, amount));
                    player.CarriedItem -= amount;
                }

                if (player.CarriedItem.Count <= 0)
                    break;
            }

            player.DraggedSlots.Clear();
            return;
        }

        switch (state)
        {
            case DraggingState.AddSlotLeft:
                if (!player.DraggedSlots.Contains(clickedSlot))
                    player.DraggedSlots.Add(clickedSlot);
                break;
            case DraggingState.AddSlotRight:
                container.SetItem(clickedSlot, player.CarriedItem);
                player.CarriedItem -= 1;
                break;
            case DraggingState.AddSlotMiddle:
                container.SetItem(clickedSlot, new(player.CarriedItem, player.CarriedItem.MaxStackSize));
                break;
            default:
                break;
        }
    }

    private static void HandleThrow(ContainerClickEventArgs args)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var button = args.Button;

        if (clickedSlot == OutsideInventory)
            return;

        ThrowItem(player, container, clickedSlot, button);
    }

    private static void ThrowItem(IPlayer player, BaseContainer container, short clickedSlot, sbyte button, bool forPlayer = false)
    {
        ItemStack? removedItem;

        var amountToRemove = button == 0 ? 1 : 64;

        if (forPlayer)
        {
            player.CarriedItem -= amountToRemove;
            removedItem = player.CarriedItem;
        }
        else
            container.RemoveItem(clickedSlot, amountToRemove, out removedItem);

        if (removedItem == null)
            return;

        var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = removedItem,
            Glowing = true,
            World = player.World,
            Position = loc
        };

        var lookDir = player.GetLookDirection();
        var vel = Velocity.FromDirection(loc, lookDir);

        //TODO Get this shooting out from the player properly.
        player.World.PacketBroadcaster.QueuePacketToWorld(player.World, new AddEntityPacket
        {
            EntityId = item.EntityId,
            Uuid = item.Uuid,
            Type = EntityType.Item,
            Position = item.Position,
            Pitch = 0,
            Yaw = 0,
            Data = 1,
            Velocity = vel
        });
        player.World.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEntityDataPacket
        {
            EntityId = item.EntityId,
            Entity = item
        });
    }

    private static void HandlePickup(ContainerClickEventArgs args)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var clickedItem = args.Item;
        var button = args.Button;

        if (!player.CarriedItem.IsNullOrAir())
        {
            if (button == 0)
            {
                if (clickedSlot == OutsideInventory)
                {
                    ThrowItem(player, container, clickedSlot, button, true);
                    return;
                }

                if (HandlePickupDirectItem(container, clickedSlot, player))
                    return;


                container.SetItem(clickedSlot, player.CarriedItem);
                player.CarriedItem = clickedItem;
            }
            else if (button == 1)
            {
                if (clickedSlot == OutsideInventory)
                    return;

                ref var itemInSlot = ref container.GetItem(clickedSlot);

                if (itemInSlot.IsNullOrAir())
                {
                    container.SetItem(clickedSlot, new(player.CarriedItem, 1));
                    player.CarriedItem -= 1;
                }
                else if (itemInSlot == player.CarriedItem && itemInSlot.Count < itemInSlot.MaxStackSize)
                {
                    itemInSlot += 1;
                    player.CarriedItem -= 1;
                }
            }
        }
        else if (!clickedItem.IsNullOrAir())
        {
            player.CarriedItem = clickedItem;
            container.RemoveItem(clickedSlot);
        }
    }

    /// <summary>
    /// Handles merging a carried item stack with a stack in a container slot.
    /// </summary>
    /// <returns>True if the items were successfully merged, false otherwise.</returns>
    private static bool HandlePickupDirectItem(BaseContainer container, short clickedSlot, IPlayer player)
    {
        ref var clickedItem = ref container.GetItem(clickedSlot);
        var carriedItem = player.CarriedItem;

        if (clickedItem.IsNullOrAir() || carriedItem.IsNullOrAir() || clickedItem != carriedItem)
            return false;

        int spaceAvailable = clickedItem.MaxStackSize - clickedItem.Count;
        if (spaceAvailable <= 0)
            return false;

        int amountToTransfer = Math.Min(spaceAvailable, carriedItem.Count);

        if (amountToTransfer > 0)
        {
            clickedItem += amountToTransfer;
            carriedItem -= amountToTransfer;

            if (carriedItem.Count <= 0)
                player.CarriedItem = null;

            return true;
        }

        return false;
    }

    private static void HandleSwap(ContainerClickEventArgs args)
    {
        var carriedItem = args.Item;
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var button = args.Button;

        var localSlot = button + 36;

        var currentItem = player.Inventory.GetItem(localSlot);

        if (carriedItem != null)
        {
            if (currentItem.IsAir)
                container.RemoveItem(clickedSlot);
            else
                container.SetItem(clickedSlot, currentItem);

            player.Inventory.SetItem(localSlot, carriedItem);

            return;
        }

        container.SetItem(clickedSlot, currentItem);

        player.Inventory.RemoveItem(localSlot);
    }
}
