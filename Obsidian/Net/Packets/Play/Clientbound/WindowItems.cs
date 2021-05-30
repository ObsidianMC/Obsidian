using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class WindowItems : IClientboundPacket
    {
        [Field(0)]
        public byte WindowId { get; }

        [Field(2), CountType(typeof(short))]
        public List<ItemStack> Items { get; }

        public int Id => 0x13;

        public WindowItems(byte windowId, List<ItemStack> items)
        {
            WindowId = windowId;
            Items = items;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
