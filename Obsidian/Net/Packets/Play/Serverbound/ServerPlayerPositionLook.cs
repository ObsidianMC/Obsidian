using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class ServerPlayerPositionLook : IPacket
    {
        [Field(0), Absolute]
        public PositionF Position { get; set; }

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public float Yaw { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public int Id => 0x34;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Position = await stream.ReadAbsolutePositionFAsync();
            this.Pitch = await stream.ReadFloatAsync();
            this.Yaw = await stream.ReadFloatAsync();
            this.OnGround = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(server, this.Position, this.Yaw, this.Pitch, this.OnGround);
        }
    }
}
