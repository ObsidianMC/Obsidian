using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityMovement : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        public int Id => 0x2A;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}