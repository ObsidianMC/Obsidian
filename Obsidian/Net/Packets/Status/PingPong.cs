using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Status
{
    public partial class PingPong : IClientboundPacket, IServerboundPacket
    {
        [Field(0)]
        public long Payload { get; private set; }

        public int Id => 0x01;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}
