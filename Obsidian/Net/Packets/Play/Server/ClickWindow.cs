using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Items;
using Obsidian.Serializer.Attributes;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
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

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            var inventory = this.WindowId > 0 ? player.OpenedInventory : player.Inventory;

            int subBy = this.ClickedSlot switch
            {
                _ when this.ClickedSlot > inventory.Size - 1 && (this.ClickedSlot >= 27 && this.ClickedSlot <= 62) => 18,
                _ when this.ClickedSlot > inventory.Size - 1 && (this.ClickedSlot >= 54 && this.ClickedSlot <= 89) => 45,
                _ when this.ClickedSlot > inventory.Size - 1 && (this.ClickedSlot >= 10 && this.ClickedSlot <= 45) => 1,
                _ when this.ClickedSlot <= inventory.Size - 1 => 0,
                _ => 0,
            };

            if (subBy > 0)
                inventory = player.Inventory;


            switch (this.Mode)
            {
                case InventoryOperationMode.MouseClick:
                    await this.HandleMouseClick(inventory, server, player, subBy);
                    break;

                case InventoryOperationMode.ShiftMouseClick:
                case InventoryOperationMode.NumberKeys:
                case InventoryOperationMode.MiddleMouseClick:
                    break;

                case InventoryOperationMode.Drop:
                    {
                        //If clicked slot is -999 that means they clicked outside the inventory
                        if (this.ClickedSlot != -999)
                        {
                            if (this.Button == 0)
                                inventory.RemoveItem(this.ClickedSlot - subBy);
                            else
                                inventory.RemoveItem(this.ClickedSlot - subBy, 64);
                        }
                        break;
                    }
                case InventoryOperationMode.MouseDrag:
                    this.HandleDragClick(inventory, server, player, subBy);
                    break;

                case InventoryOperationMode.DoubleClick:
                default:
                    break;
            }
        }

        private async Task HandleMouseClick(Inventory inventory, Obsidian.Server server, Player player, int subBy)
        {
            if (this.Item?.Id > 0)
            {
                var evt = await server.Events.InvokeInventoryClickAsync(new InventoryClickEventArgs(player, inventory, this.Item)
                {
                    Slot = this.ClickedSlot
                });

                if (evt.Cancel)
                    return;

                player.LastClickedItem = this.Item;

                Console.WriteLine($"Last clicked item: {player.LastClickedItem.Type}");
            }

            if (this.Button == 0)
            {
                if (player.LastClickedItem.Id > 0 && this.Item == null)
                {
                    inventory.SetItem(this.ClickedSlot - subBy, player.LastClickedItem);

                    //Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                    player.LastClickedItem = this.Item;

                    return;
                }

                player.LastClickedItem = inventory.GetItem(this.ClickedSlot - subBy);

                inventory.SetItem(this.ClickedSlot - subBy, null);
            }
            else
            {
                if (player.LastClickedItem.Id > 0 && this.Item == null)
                {
                    inventory.SetItem(this.ClickedSlot - subBy, player.LastClickedItem);

                    //Globals.PacketLogger.LogDebug($"{(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                    player.LastClickedItem = this.Item;

                    return;
                }

                player.LastClickedItem = inventory.GetItem(this.ClickedSlot - subBy);

                inventory.SetItem(this.ClickedSlot - subBy, null);
            }
        }

        private void HandleDragClick(Inventory inventory, Obsidian.Server server, Player player, int subBy)
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

                    //creative copy
                    inventory.SetItem(this.ClickedSlot - subBy, this.Item);
                }
                else
                {
                    if (this.Button != 1 || this.Button != 5)
                        return;

                    //survival painting
                    inventory.SetItem(this.ClickedSlot - subBy, this.Item);
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
