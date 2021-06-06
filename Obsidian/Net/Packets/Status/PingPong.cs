using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Status
{
    public partial class PingPong : IClientboundPacket
    {
        [Field(0)]
        public long Payload;

        public int Id => 0x01;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}