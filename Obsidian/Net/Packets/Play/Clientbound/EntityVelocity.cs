using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityVelocity : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Velocity Velocity { get; set; }

        public int Id => 0x46;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
