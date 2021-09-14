using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    // Source: https://wiki.vg/index.php?title=Protocol&oldid=14889#Click_Window
    public partial class ClickWindow : IServerboundPacket
    {
        /// <summary>
        /// The ID of the window which was clicked. 0 for player inventory.
        /// </summary>
        [Field(0)]
        public byte WindowId { get; private set; }

        /// <summary>
        /// The clicked slot number
        /// </summary>
        [Field(1)]
        public short ClickedSlot { get; private set; }

        /// <summary>
        /// The button used in the click
        /// </summary>
        [Field(2)]
        public sbyte Button { get; private set; }

        /// <summary>
        /// A unique number for the action
        /// </summary>
        [Field(3)]
        public short ActionNumber { get; private set; }

        /// <summary>
        /// Inventory operation mode
        /// </summary>
        [Field(4), ActualType(typeof(int)), VarLength]
        public InventoryOperationMode Mode { get; private set; }

        /// <summary>
        /// The clicked slot. Has to be empty (item ID = -1) for drop mode.
        /// </summary>
        [Field(5)]
        public ItemStack Item { get; private set; }

        public int Id => 0x08;

        private const int Outsideinventory = -999;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            var inventory = player.OpenedInventory ?? player.Inventory;

            var (value, forPlayer) = ClickedSlot.GetDifference(inventory.Size);

            if (WindowId == 0 && player.LastClickedBlock.Is(Material.EnderChest) && ClickedSlot >= 27 && ClickedSlot <= 62 || forPlayer)
                inventory = player.Inventory;

            switch (Mode)
            {
                case InventoryOperationMode.MouseClick:
                    await HandleMouseClick(inventory, server, player, value);
                    break;

                case InventoryOperationMode.ShiftMouseClick:
                    {
                        if (Item == null)
                            return;

                        inventory.RemoveItem(value);
                        player.Inventory.AddItem(Item);
                        break;
                    }

                case InventoryOperationMode.NumberKeys:
                    {
                        var localSlot = Button + 36;

                        var currentItem = player.Inventory.GetItem(localSlot);

                        if (currentItem.IsAir() && Item != null)
                        {
                            inventory.RemoveItem(value);

                            player.Inventory.SetItem(localSlot, Item);
                        }
                        else if (!currentItem.IsAir() && Item != null)
                        {
                            inventory.SetItem(value, currentItem);

                            player.Inventory.SetItem(localSlot, Item);
                        }
                        else
                        {
                            inventory.SetItem(value, currentItem);

                            player.Inventory.RemoveItem(localSlot);
                        }

                        break;
                    }

                case InventoryOperationMode.MiddleMouseClick:
                    break;

                case InventoryOperationMode.Drop:
                    {
                        // TODO drop actual item
                        if (ClickedSlot != Outsideinventory)
                        {
                            if (Button == 0)
                            {
                                inventory.RemoveItem(value);
                            }
                            else
                            {
                                inventory.RemoveItem(value, 64);
                            }
                        }
                        break;
                    }

                case InventoryOperationMode.MouseDrag:
                    HandleDragClick(inventory, player, value);
                    break;

                case InventoryOperationMode.DoubleClick:
                    {
                        if (Item == null || Item.Count >= 64)
                            return;

                        var item = Item;

                        (ItemStack item, int index) selectedItem = (null, 0);

                        var items = inventory.Items
                            .Select((item, index) => (item, index))
                            .Where(tuple => tuple.item.Type == item.Type)
                            .OrderByDescending(x => x.index);

                        foreach (var (invItem, index) in items)
                        {
                            if (invItem != item)
                                continue;

                            var copyItem = invItem;

                            var finalCount = item.Count + copyItem.Count;

                            if (finalCount <= 64)
                            {
                                item += copyItem.Count;

                                copyItem -= finalCount;
                            }
                            else if (finalCount > 64)
                            {
                                var difference = finalCount - 64;

                                copyItem -= difference;

                                item += difference;
                            }

                            selectedItem = (copyItem, index);
                            break;
                        }

                        inventory.SetItem((short)selectedItem.index, selectedItem.item);
                        break;
                    }
            }
        }

        private async Task HandleMouseClick(Inventory inventory, Server server, Player player, int value)
        {
            if (!Item.IsAir())
            {
                var @event = await server.Events.InvokeInventoryClickAsync(new InventoryClickEventArgs(player, inventory, Item)
                {
                    Slot = value
                });

                if (@event.Cancel)
                    return;

                player.LastClickedItem = Item;

                inventory.SetItem(value, null);
            }
            else
            {
                if (Button == 0)
                {
                    inventory.SetItem(value, player.LastClickedItem);

                    // if (!inventory.OwnedByPlayer)
                    //    Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                    player.LastClickedItem = Item;
                }
                else
                {
                    inventory.SetItem(value, player.LastClickedItem);

                    // if (!inventory.OwnedByPlayer)
                    //    Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                    player.LastClickedItem = Item;
                }
            }
        }

        private void HandleDragClick(Inventory inventory, Player player, int value)
        {
            if (ClickedSlot == Outsideinventory)
            {
                if (Button == 0 || Button == 4 || Button == 8)
                    player.isDragging = true;
                else if (Button == 2 || Button == 6 || Button == 10)
                    player.isDragging = false;
            }
            else if (player.isDragging)
            {
                if (player.Gamemode == Gamemode.Creative)
                {
                    if (Button != 9)
                        return;

                    inventory.SetItem(value, Item);
                }
                else
                {
                    // 1 = left mouse
                    // 5 = right mouse
                    if (Button != 1 || Button != 5)
                        return;

                    inventory.SetItem(value, Item);
                }
            }
        }
    }

    public enum InventoryOperationMode : int
    {
        MouseClick,
        ShiftMouseClick,
        NumberKeys,
        MiddleMouseClick,
        Drop,
        MouseDrag,
        DoubleClick
    }
}
