using Obsidian.Chat;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public class OpenWindow : Packet
    {
        [Field(0)]
        public int WindowId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public WindowType Type { get; set; }

        [Field(2)]
        public ChatMessage Title { get; set; }

        public OpenWindow() : base(0x2F) { }
    }

    //Do not mess up the order this is how its supposed to be ordered
    internal enum WindowType : int
    {
        Generic9x1,
        Generic9x2,
        Generic9x3,
        Generic9x4,
        Generic9x5,
        Generic9x6,
        Generic3x3,

        Anvil,
        Beacon,
        BlastFurnace,
        BrewingStand,
        Crafting,
        Enchantment,
        Furnace,
        Grindstone,
        Hopper,
        Lectern,
        Loom,
        Merchant,
        ShulkerBox,
        Smoker,
        CartographyTable,
        Stonecutter
    }
}
