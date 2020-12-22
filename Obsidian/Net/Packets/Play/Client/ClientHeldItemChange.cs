using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class ClientHeldItemChange : IPacket
    {
        [Field(0)]
        public byte Slot { get; private set; }

        public int Id => 0x3F;

        private ClientHeldItemChange()
        {
        }

        public ClientHeldItemChange(byte slot)
        {
            this.Slot = slot;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
