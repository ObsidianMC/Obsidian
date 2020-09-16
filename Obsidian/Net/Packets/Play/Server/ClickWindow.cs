using Obsidian.Items;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol&oldid=14889#Click_Window
    /// </summary>
    public class ClickWindow : Packet
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
        public Slot Item { get; set; }

        public ClickWindow() : base(0x09) { }
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
