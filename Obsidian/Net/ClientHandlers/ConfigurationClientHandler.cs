using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets.Common;

namespace Obsidian.Net.ClientHandlers;
internal sealed class ConfigurationClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case 0:
                return await HandleFromPoolAsync<ClientInformationPacket>(buffer.Data);
            case 2:
                return await HandleFromPoolAsync<CustomPayloadPacket>(buffer.Data);
            case 3:
                return await HandleFromPoolAsync<Packets.Configuration.Serverbound.FinishConfigurationPacket>(buffer.Data);
            case 4:
                return await HandleFromPoolAsync<KeepAlivePacket>(buffer.Data);
            case 6:
                return await HandleFromPoolAsync<ResourcePackPacket>(buffer.Data);
            default:
                this.Client.Logger.LogWarning("Configuration Packet({id}) {name} is not being handled.", id, PacketsRegistry.Configuration.ServerboundNames[id]);
                break;
        }

        return false;
    }
}
