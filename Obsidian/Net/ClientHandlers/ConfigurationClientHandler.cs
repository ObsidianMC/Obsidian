using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Configuration;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Net.ClientHandlers;
internal sealed class ConfigurationClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;

        switch (id)
        {
            case 0x00:
                return await this.HandleFromPoolAsync<ClientInformationPacket>(data);
            case 0x01://Cookies
                break;
            case 0x02:
                return await HandleFromPoolAsync<PluginMessagePacket>(data);
            case 0x03:
                return await HandleFromPoolAsync<FinishConfigurationPacket>(data);
            case 0x04:
                return await HandleFromPoolAsync<KeepAlivePacket>(data);
            case 0x05://pong useless
                break;
            case 0x06:
                return await HandleFromPoolAsync<ResourcePackResponse>(data);
            default:
                this.Client.Logger.LogWarning("Packet with id {id} is not being handled.", id);
                break;
        }

        return false;
    }
}
