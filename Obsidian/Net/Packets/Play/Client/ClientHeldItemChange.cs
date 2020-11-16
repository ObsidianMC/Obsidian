using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class ClientHeldItemChange : IPacket
    {
        [Field(0)]
        public byte Slot { get; }

        public int Id => 0x3F;

        public ClientHeldItemChange(byte slot)
        {
            this.Slot = slot;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
