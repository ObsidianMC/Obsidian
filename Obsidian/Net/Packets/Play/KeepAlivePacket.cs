using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class KeepAlivePacket : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public long KeepAliveId { get; private set; }

    public int Id => 0x1E;

    public KeepAlivePacket()
    {
    }

    public KeepAlivePacket(long id)
    {
        KeepAliveId = id;
    }

    public ValueTask HandleAsync(Server server, Player player)
    {
        server.Logger.LogDebug($"Keep alive {player.Username} [{KeepAliveId}] Missed: {player.client.missedKeepAlives - 1}"); // Missed is 1 more bc we just handled one

        player.client.missedKeepAlives = 0;

        return ValueTask.CompletedTask;
    }
}
