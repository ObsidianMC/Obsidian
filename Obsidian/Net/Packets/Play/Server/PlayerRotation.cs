using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public class PlayerRotation : IPacket
    {
        [Field(0)]
        public float Yaw { get => this.yaw; set => this.yaw = (value % 360 + 360) % 360; }

        private float yaw;

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public bool OnGround { get; set; }

        public int Id => 0x14;

        public PlayerRotation() { }

        public PlayerRotation(float yaw, float pitch, bool onground)
        {
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onground;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Yaw = await stream.ReadFloatAsync();
            this.Pitch = await stream.ReadFloatAsync();
            this.OnGround = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            await player.UpdateAsync(server, this.Yaw, this.Pitch, this.OnGround);
        }

    }
}