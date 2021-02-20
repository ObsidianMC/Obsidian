using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SetSlot : IPacket
    {
        /// <summary>
        /// 0 for player inventory. -1 For the currently dragged item.
        /// If the window ID is set to -2, then any slot in the inventory can be used but no add item animation will be played.
        /// </summary>
        [Field(0)]
        public byte WindowId { get; set; } = 0;

        /// <summary>
        /// Can be -1 to set the currently dragged item.
        /// </summary>
        [Field(1)]
        public short Slot { get; set; }

        [Field(2)]
        public ItemStack SlotData { get; set; }

        public int Id => 0x15;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
