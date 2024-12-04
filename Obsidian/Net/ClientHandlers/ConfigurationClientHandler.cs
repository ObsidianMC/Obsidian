using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets.Common;

namespace Obsidian.Net.ClientHandlers;
internal sealed class ConfigurationClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;

        switch (id)
        {
            case 0:
                return await HandleFromPoolAsync<ClientInformationPacket>(data);
            case 1:
                return await HandleFromPoolAsync<CustomPayloadPacket>(data);
            case 3:
                return await HandleFromPoolAsync<Packets.Configuration.Serverbound.FinishConfigurationPacket>(data);
            case 4:
                return await HandleFromPoolAsync<KeepAlivePacket>(data);
            case 6:
                return await HandleFromPoolAsync<ResourcePackPacket>(data);
            default:
                this.Client.Logger.LogWarning("Packet with id {id} is not being handled.", id);
                break;
        }

        return false;
    }
}
