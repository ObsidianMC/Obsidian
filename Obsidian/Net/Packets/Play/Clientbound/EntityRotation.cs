using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityRotation : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Angle Yaw { get; set; }

        [Field(2)]
        public Angle Pitch { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public int Id => 0x29;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
