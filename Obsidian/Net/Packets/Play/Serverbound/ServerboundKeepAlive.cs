using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class ServerboundKeepAlive : IServerboundPacket
    {
        [Field(0)]
        public long KeepAliveId { get; private set; }

        public int Id => 0x0F;

        public ServerboundKeepAlive()
        {
        }

        public ServerboundKeepAlive(long id)
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
