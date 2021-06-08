using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PlayerPositionAndRotation : IServerboundPacket
    {
        [Field(0), VectorFormat(typeof(double))]
        public VectorF Position { get; set; }

        [Field(1)]
        public float Yaw { get; set; }

        [Field(2)]
        public float Pitch { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public int Id => 0x34;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(server, Position, Yaw, Pitch, OnGround);
        }
    }
}
