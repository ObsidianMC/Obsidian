using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class ClientHeldItemChange : IClientboundPacket
    {
        [Field(0)]
        public byte Slot { get; }

        public int Id => 0x3F;

        public ClientHeldItemChange(byte slot)
        {
            Slot = slot;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
