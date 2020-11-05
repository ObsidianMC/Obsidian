using Obsidian.Serializer.Attributes;
using Obsidian.API;
using System.Threading.Tasks;
using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Server
{
    public class ServerPlayerPositionLook : IPacket
    {
        [Field(0, true)]
        public Position Position { get; set; }

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public float Yaw { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public int Id => 0x34;

        public ServerPlayerPositionLook() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Position = await stream.ReadAbsolutePositionAsync();
            this.Pitch = await stream.ReadFloatAsync();
            this.Yaw = await stream.ReadFloatAsync();
            this.OnGround = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            await player.UpdateAsync(server, this.Position, this.Yaw, this.Pitch, this.OnGround);
        }
    }
}
