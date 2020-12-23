using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class WindowItems : IPacket
    {
        [Field(0)]
        public byte WindowId { get; set; }

        [Field(1)]
        public short Count { get; set; }

        [Field(2), FixedLength(1)]
        public List<ItemStack> Items { get; set; }

        public int Id => 0x13;

        public WindowItems()
        {
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
