using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class WindowItems : IPacket
    {
        [Field(0)]
        public byte WindowId { get; set; }

        [Field(1)]
        public short Count { get; set; }

        [Field(2, Type = DataType.Array)]
        public List<ItemStack> Items { get; set; }

        public int Id => 0x13;

        public WindowItems() { }

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
