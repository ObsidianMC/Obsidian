using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class ClientboundKeepAlive : IClientboundPacket
    {
        [Field(0)]
        public long KeepAliveId { get; private set; }

        public int Id => 0x21;

        public ClientboundKeepAlive()
        {
        }

        public ClientboundKeepAlive(long id)
        {
            KeepAliveId = id;
        }

        public ValueTask HandleAsync(Server server, Player player)
        {
            Globals.PacketLogger.LogDebug($"Keep alive {player.Username} [{KeepAliveId}] Missed: {player.client.missedKeepalives - 1}"); // Missed is 1 more bc we just handled one

            player.client.missedKeepalives = 0;

            return ValueTask.CompletedTask;
        }
    }
}