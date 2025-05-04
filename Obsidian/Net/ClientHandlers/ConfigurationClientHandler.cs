using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Configuration.Clientbound;

namespace Obsidian.Net.ClientHandlers;
internal sealed class ConfigurationClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case 0:
                return await HandleFromPoolAsync<ClientInformationPacket>(buffer);
            case 2:
                return await HandleFromPoolAsync<CustomPayloadPacket>(buffer);
            case 3:
                return await HandleFromPoolAsync<Packets.Configuration.Serverbound.FinishConfigurationPacket>(buffer);
            case 4:
                return await HandleFromPoolAsync<KeepAlivePacket>(buffer);
            case 6:
                return await HandleFromPoolAsync<ResourcePackPacket>(buffer);
            default:
                this.Client.Logger.LogWarning("Configuration Packet({id}) {name} is not being handled.", id, PacketsRegistry.Configuration.ServerboundNames[id]);
                break;
        }

        return false;
    }
}
