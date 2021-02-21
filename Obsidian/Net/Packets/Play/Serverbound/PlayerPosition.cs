using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PlayerPosition : IPacket
    {
        [Field(0), Absolute]
        public PositionF Position { get; set; }

        [Field(1)]

        public bool OnGround { get; private set; }

        public int Id => 0x12;

        public PlayerPosition()
        {
        }

        public PlayerPosition(PositionF pos, bool onground)
        {
            this.Position = pos;
            this.OnGround = onground;
        }

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Position = await stream.ReadAbsolutePositionFAsync();
            this.OnGround = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(server, this.Position, this.OnGround);
            await player.World.UpdateClientChunksAsync(player.client);
        }
    }
}