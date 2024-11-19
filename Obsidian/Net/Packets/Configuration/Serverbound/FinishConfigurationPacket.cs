using Microsoft.Extensions.Logging;
using Obsidian.Entities;

namespace Obsidian.Net.Packets.Configuration.Serverbound;
public sealed partial class FinishConfigurationPacket
{
    //TODO move connect logic into here
    public async override ValueTask HandleAsync(Server server, Player player)
    {
        player.client.Logger.LogDebug("Got finished configuration");

        await player.client.ConnectAsync();
    }
}
