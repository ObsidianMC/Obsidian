using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityHeadLook : IClientboundPacket
    {

        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Angle HeadYaw { get; set; }

        public int Id => 0x3A;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
