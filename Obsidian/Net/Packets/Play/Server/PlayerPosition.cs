using Obsidian.Serializer.Attributes;
using Obsidian.API;
using System.Threading.Tasks;
using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Server
{
    public class PlayerPosition : IPacket
    {
        [Field(0, true)]
        public Position Position { get; set; }

        [Field(1)]

        public bool OnGround { get; private set; }

        public int Id => 0x12;

        public PlayerPosition() { }

        public PlayerPosition(Position pos, bool onground)
        {
            this.Position = pos;
            this.OnGround = onground;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Position = await stream.ReadAbsolutePositionAsync();
            this.OnGround = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            await player.UpdateAsync(server, this.Position, this.OnGround);
            await player.World.UpdateClientChunksAsync(player.client);
        }
    }
}