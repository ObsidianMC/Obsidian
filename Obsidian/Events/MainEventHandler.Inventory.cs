using Microsoft.Extensions.Logging;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

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
        container.SetItem(9, result);

       
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
            item.Count -= amountTaken;

            if (item.Count == 0)
                container.RemoveItem(i);

            carriedItem.Count += amountTaken;
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
                item.Count -= amountTaken;

                if (item.Count == 0)
                    player.Inventory.RemoveItem(i);

                carriedItem.Count += amountTaken;
                amountNeeded -= amountTaken;

                if (amountNeeded == 0)
                    break;
            }
        }
    }

    private static void HandleQuickCraft(ContainerClickEventArgs args)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var button = args.Button;
        var carriedItem = args.Item;

        if (clickedSlot == OutsideInventory)
        {
            player.IsDragging = button switch
            {
                0 or 4 or 8 => true,
                2 or 6 or 10 => false,
                _ => player.IsDragging
            };
        }
        else if (player.IsDragging)
        {
            if (player.Gamemode == Gamemode.Creative)
            {
                if (button != 9)
                    return;

                container.SetItem(clickedSlot, carriedItem);
            }
            else
            {
                // 1 = left mouse
                // 5 = right mouse
                if (button != 1 && button != 5)
                    return;

                container.SetItem(clickedSlot, carriedItem);
            }
        }
    }

    private static void HandleThrow(ContainerClickEventArgs args)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var button = args.Button;

        if (clickedSlot != OutsideInventory)
        {
            ItemStack? removedItem;
            if (button == 0)
                container.RemoveItem(clickedSlot, 1, out removedItem);
            else
                container.RemoveItem(clickedSlot, 64, out removedItem);

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
    }

    private static void HandlePickup(ContainerClickEventArgs args)
    {
        var clickedSlot = args.ClickedSlot;
        var container = args.Container;
        var player = args.Player;
        var clickedItem = args.Item;
        var button = args.Button;
        var logger = player.Client.Logger;

        if (!player.CarriedItem.IsNullOrAir())
        {
            logger.LogInformation("Item count: {count} - {type}", player.CarriedItem.Count, player.CarriedItem.Holder.UnlocalizedName);
            switch (button)
            {
                case 0:
                    container.SetItem(clickedSlot, player.CarriedItem);
                    player.CarriedItem = null;
                    break;
                case 1:
                    var newItem = player.CarriedItem - 1;
                    player.CarriedItem = newItem;

                    container.SetItem(clickedSlot, new(newItem));
                    logger.LogInformation("Setting item in container slot: {slot} - {item}", clickedSlot, player.CarriedItem.Holder.UnlocalizedName);
                    break;
                default:
                    break;
            }

            return;
        }

        logger.LogInformation("Carried item is null or air");

        if (clickedItem.IsNullOrAir())
            return;

        logger.LogInformation("Picked up item: {item}", clickedItem.Holder.UnlocalizedName);

        player.CarriedItem = clickedItem;
        container.RemoveItem(clickedSlot);
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
