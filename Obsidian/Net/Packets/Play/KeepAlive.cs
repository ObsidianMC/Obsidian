using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class KeepAlive : ISerializablePacket
    {
        [Field(0)]
        public long KeepAliveId { get; set; }

        public int Id => 0x1F;

        public KeepAlive()
        {
        }

        public KeepAlive(long id)
        {
            this.KeepAliveId = id;
        }

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.KeepAliveId = await stream.ReadLongAsync();
        }

        public Task HandleAsync(Server server, Player player)
        {
            Globals.PacketLogger.LogDebug($"Successfully kept alive player {player.Username} with ka id " +
                       $"{this.KeepAliveId} previously missed {player.client.missedKeepalives - 1} ka's"); // missed is 1 more bc we just handled one

            player.client.missedKeepalives = 0;

            return Task.CompletedTask;
        }
    }
}