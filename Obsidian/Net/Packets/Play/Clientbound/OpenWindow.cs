using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class OpenWindow : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int WindowId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public WindowType Type { get; set; }

        [Field(2)]
        public ChatMessage Title { get; set; }

        public int Id => 0x2D;

        public OpenWindow() { }

        public OpenWindow(Inventory inventory)
        {
            this.Title = inventory.Title;

            if (Enum.TryParse<WindowType>(inventory.Type.ToString(), true, out var type))
                this.Type = type;
            else if (Enum.TryParse($"generic9x{inventory.Size / 9}", true, out type))
                this.Type = type;

            this.WindowId = inventory.Id;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;

        public override string ToString() => $"{this.WindowId}:{this.Type}";
    }

    //Do not mess up the order this is how its supposed to be ordered
    public enum WindowType : int
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
