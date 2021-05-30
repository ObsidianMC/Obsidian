using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PlayerPositionAndRotation : IServerboundPacket
    {
        [Field(0), Absolute]
        public VectorF Position { get; set; }

        [Field(1)]
        public float Yaw { get; set; }

        [Field(2)]
        public float Pitch { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public int Id => 0x34;

        public Task HandleAsync(Server server, Player player) => player.UpdateAsync(server, this.Position, this.Yaw, this.Pitch, this.OnGround);
    }
}
