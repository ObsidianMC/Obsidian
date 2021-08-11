using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class KeepAlive : IClientboundPacket, IServerboundPacket
    {
        [Field(0)]
        public long KeepAliveId { get; private set; }

        public int Id => 0x1F;

        public KeepAlive()
        {
        }

        public KeepAlive(long id)
        {
            KeepAliveId = id;
        }

        public ValueTask HandleAsync(Server server, Player player)
        {
            if (player.client.missedKeepalives > 1)
                Globals.PacketLogger.LogDebug($"Keep alive {player.Username} [{KeepAliveId}] Missed: {player.client.missedKeepalives - 1}"); // Missed is 1 more bc we just handled one

            player.client.missedKeepalives = 0;

            return ValueTask.CompletedTask;
        }
    }
}
