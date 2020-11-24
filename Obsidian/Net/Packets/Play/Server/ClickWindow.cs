using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Serializer.Attributes;
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
            this.Mode = (InventoryOperationMode)await stream.ReadIntAsync();
            this.Item = await stream.ReadSlotAsync();
        }

        public Task HandleAsync(Obsidian.Server server, Player player)
        {
            if (this.WindowId == 0)
            {

                //This is the player inventory
                switch (this.Mode)
                {
                    case InventoryOperationMode.MouseClick://TODO InventoryClickEvent
                        {
                            if (this.Button == 0)
                            {
                                player.Inventory.RemoveItem(this.ClickedSlot, 64);
                            }
                            else
                            {
                                player.Inventory.RemoveItem(this.ClickedSlot, (short)(this.Item.Count / 2));
                            }
                            break;
                        }

                    case InventoryOperationMode.ShiftMouseClick:
                        break;
                    case InventoryOperationMode.NumberKeys:
                        break;
                    case InventoryOperationMode.MiddleMouseClick:
                        break;
                    case InventoryOperationMode.Drop:
                        {
                            //If clicked slot is -999 that means they clicked outside the inventory
                            if (this.ClickedSlot != -999)
                            {
                                if (this.Button == 0)
                                    player.Inventory.RemoveItem(this.ClickedSlot);
                                else
                                    player.Inventory.RemoveItem(this.ClickedSlot, 64);
                            }
                            break;
                        }
                    case InventoryOperationMode.MouseDrag:
                        {
                            if (this.ClickedSlot == -999)
                            {
                                if (this.Button == 0 || this.Button == 4 || this.Button == 8)
                                {
                                    player.IsDragging = true;
                                }
                                else if (this.Button == 2 || this.Button == 6 || this.Button == 10)
                                {
                                    player.IsDragging = false;
                                }
                            }
                            else if (player.IsDragging)
                            {
                                if (player.Gamemode == Gamemode.Creative)
                                {
                                    if (this.Button != 9)
                                        break;

                                    //creative copy
                                    player.Inventory.SetItem(this.ClickedSlot, this.Item);
                                }
                                else
                                {
                                    if (this.Button != 1 || this.Button != 5)
                                        break;

                                    //survival painting
                                    player.Inventory.SetItem(this.ClickedSlot, this.Item);
                                }
                            }
                            else
                            {
                                //It shouldn't get here
                            }

                            break;
                        }
                    case InventoryOperationMode.DoubleClick:
                        break;
                    default:
                        break;
                }
            }
            else
            {

            }

            return Task.CompletedTask;
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
