using Microsoft.Extensions.Logging;
using Obsidian.Entities;

namespace Obsidian.Net.Packets.Configuration;
public sealed partial class FinishConfigurationPacket : IServerboundPacket, IClientboundPacket
{
    public static FinishConfigurationPacket Default { get; } = new();

    public int Id => 0x03;

    //TODO move connect logic into here
    public async ValueTask HandleAsync(Server server, Player player)
    {
        player.client.Logger.LogDebug("Got finished configuration");

        await player.client.ConnectAsync();
    }

    public void Populate(byte[] data) { }
    public void Populate(MinecraftStream stream) { }

    public void Serialize(MinecraftStream stream) => this.WritePacketId(stream);
}
