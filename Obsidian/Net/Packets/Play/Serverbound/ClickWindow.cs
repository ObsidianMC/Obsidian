using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.Extensions;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol&oldid=14889#Click_Window
    /// </summary>
    public class ClickWindow : IPacket
    {
        /// <summary>
        /// The ID of the window which was clicked. 0 for player inventory.
        /// </summary>
        [Field(0)]
        public byte WindowId { get; set; }

        /// <summary>
        /// The clicked slot number
        /// </summary>
        [Field(1)]
        public short ClickedSlot { get; set; }

        /// <summary>
        /// The button used in the click
        /// </summary>
        [Field(2)]
        public sbyte Button { get; set; }

        /// <summary>
        /// A unique number for the action
        /// </summary>
        [Field(3)]
        public short ActionNumber { get; set; }

        /// <summary>
        /// Inventory operation mode
        /// </summary>
        [Field(4)]
        public InventoryOperationMode Mode { get; set; }

        /// <summary>
        /// The clicked slot. Has to be empty (item ID = -1) for drop mode.
        /// </summary>
        [Field(5)]
        public ItemStack Item { get; set; }

        public int Id => 0x09;

        public ClickWindow() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.WindowId = await stream.ReadUnsignedByteAsync();
            this.ClickedSlot = await stream.ReadShortAsync();
            this.Button = await stream.ReadByteAsync();
            this.ActionNumber = await stream.ReadShortAsync();
            this.Mode = (InventoryOperationMode)await stream.ReadVarIntAsync();
            this.Item = await stream.ReadSlotAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            var inventory = player.OpenedInventory;

            var (value, forPlayer) = this.ClickedSlot.GetDifference(inventory.Size);

            if (this.WindowId == 0 && player.LastClickedBlock.Type == Materials.EnderChest && this.ClickedSlot >= 27 && this.ClickedSlot <= 62 || forPlayer)
                inventory = player.Inventory;

            switch (this.Mode)
            {
                case InventoryOperationMode.MouseClick:
                    await this.HandleMouseClick(inventory, server, player, value);
                    break;

                case InventoryOperationMode.ShiftMouseClick:
                    {
                        if (this.Item == null)
                            return;

                        inventory.RemoveItem(value);
                        player.Inventory.AddItem(this.Item);
                        break;
                    }
                case InventoryOperationMode.NumberKeys:
                    {
                        var localSlot = this.Button + 36;

                        var currentItem = player.Inventory.GetItem(localSlot);

                        if (currentItem.IsAir() && this.Item != null)
                        {
                            inventory.RemoveItem(value);

                            player.Inventory.SetItem(localSlot, this.Item);
                        }
                        else if (!currentItem.IsAir() && this.Item != null)
                        {
                            inventory.SetItem(value, currentItem);

                            player.Inventory.SetItem(localSlot, this.Item);
                        }
                        else
                        {
                            inventory.SetItem(value, currentItem);

                            player.Inventory.RemoveItem(localSlot);
                        }

                        break;
                    }
                case InventoryOperationMode.MiddleMouseClick:
                    {
                        if (this.Item == null)
                            return;


                        break;
                    }
                case InventoryOperationMode.Drop:
                    {
                        //If clicked slot is -999 that means they clicked outside the inventory
                        //TODO: drop actual item
                        if (this.ClickedSlot != -999)
                        {
                            if (this.Button == 0)
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
                    this.HandleDragClick(inventory, server, player, value);
                    break;

                case InventoryOperationMode.DoubleClick:
                    {
                        if (this.Item == null || this.Item.Count >= 64)
                            return;

                        var item = this.Item;

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
                default:
                    break;
            }
        }

        private async Task HandleMouseClick(Inventory inventory, Server server, Player player, int value)
        {
            if (!this.Item.IsAir())
            {
                var evt = await server.Events.InvokeInventoryClickAsync(new InventoryClickEventArgs(player, inventory, this.Item)
                {
                    Slot = value
                });

                if (evt.Cancel)
                    return;

                player.LastClickedItem = this.Item;

                inventory.SetItem(value, null);
            }
            else
            {
                if (this.Button == 0)
                {
                    inventory.SetItem(value, player.LastClickedItem);

                    // if (!inventory.OwnedByPlayer)
                    //    Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                    player.LastClickedItem = this.Item;
                }
                else
                {
                    inventory.SetItem(value, player.LastClickedItem);

                    // if (!inventory.OwnedByPlayer)
                    //    Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                    player.LastClickedItem = this.Item;
                }
            }
        }

        private void HandleDragClick(Inventory inventory, Server server, Player player, int value)
        {
            if (this.ClickedSlot == -999)
            {
                if (this.Button == 0 || this.Button == 4 || this.Button == 8)
                    player.IsDragging = true;
                else if (this.Button == 2 || this.Button == 6 || this.Button == 10)
                    player.IsDragging = false;

            }
            else if (player.IsDragging)
            {
                if (player.Gamemode == Gamemode.Creative)
                {
                    if (this.Button != 9)
                        return;

                    inventory.SetItem(value, this.Item);
                }
                else
                {
                    //1 = left mouse
                    //5 = right mouse
                    if (this.Button != 1 || this.Button != 5)
                        return;

                    inventory.SetItem(value, this.Item);
                }
            }
            else
            {
                //It shouldn't get here
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
