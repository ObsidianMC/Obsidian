using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PickItem : IPacket
    {
        [Field(0)]
        public int SlotToUse { get; set; }

        public int Id => 0x18;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;
        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
