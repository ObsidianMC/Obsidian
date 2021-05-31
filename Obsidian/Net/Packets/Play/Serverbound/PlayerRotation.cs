using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PlayerRotation : IServerboundPacket
    {
        [Field(0)]
        public float Yaw { get => yaw; set => yaw = (value % 360 + 360) % 360; }

        private float yaw;

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public bool OnGround { get; set; }

        public int Id => 0x14;

        public PlayerRotation()
        {
        }

        public PlayerRotation(float yaw, float pitch, bool onground)
        {
            Yaw = yaw;
            Pitch = pitch;
            OnGround = onground;
        }

        public async ValueTask HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(server, Yaw, Pitch, OnGround);
        }
    }
}