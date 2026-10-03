using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets.Common;

namespace Obsidian.Net.ClientHandlers;
internal sealed partial class ConfigurationClientHandler : ClientHandler
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
                Log.UnhandledPacket(this.Logger, id);
                break;
        }

        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Configuration packet {PacketId} is not handled")]
        public static partial void UnhandledPacket(ILogger logger, int packetId);
    }
}
