using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class OpenWindow : ISerializablePacket
    {
        [Field(0), VarLength]
        public int WindowId { get; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public WindowType Type { get; }

        [Field(2)]
        public ChatMessage Title { get; }

        public int Id => 0x2D;

        public OpenWindow(Inventory inventory)
        {
            Title = (ChatMessage)inventory.Title;

            if (Enum.TryParse<WindowType>(inventory.Type.ToString(), true, out var type))
                Type = type;
            else if (Enum.TryParse($"generic9x{inventory.Size / 9}", true, out type))
                Type = type;

            WindowId = inventory.Id;
        }

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public override string ToString() => $"{this.WindowId}:{this.Type}";
    }

    // Do not mess up the order this is how it's supposed to be ordered
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
