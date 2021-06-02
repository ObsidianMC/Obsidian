using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PlayerPosition : IServerboundPacket
    {
        [Field(0), Absolute]
        public VectorF Position { get; set; }

        [Field(1)]

        public bool OnGround { get; private set; }

        public int Id => 0x12;

        public PlayerPosition()
        {
        }

        public PlayerPosition(VectorF pos, bool onground)
        {
            this.Position = pos;
            this.OnGround = onground;
        }

        public async ValueTask HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(this.Position, this.OnGround);
            await player.World.UpdateClientChunksAsync(player.client);
        }
    }
}