using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class WindowItems : ISerializablePacket
    {
        [Field(0)]
        public byte WindowId { get; }

        [Field(1)]
        public short Count { get; }

        [Field(2), FixedLength(1)]
        public List<ItemStack> Items { get; }

        public int Id => 0x13;

        public WindowItems(byte windowId, List<ItemStack> items)
        {
            WindowId = windowId;
            Count = (short)items.Count;
            Items = items;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
