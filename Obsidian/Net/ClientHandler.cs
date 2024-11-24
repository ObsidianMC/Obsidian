using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.API.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Utilities.Collections;

namespace Obsidian.Net;

public sealed class ClientHandler
{
    private ConcurrentDictionary<int, ServerboundPacket> Packets { get; } = new ConcurrentDictionary<int, ServerboundPacket>();
    private ServerConfiguration config;
    private readonly ILogger _logger;

    public ClientHandler(ServerConfiguration config)
    {
        this.config = config;
        var loggerProvider = new LoggerProvider(LogLevel.Error);
        _logger = loggerProvider.CreateLogger("ClientHanlder");
    }

    public void RegisterHandlers()
    {
        Packets.TryAdd(28, new MovePlayerPosPacket());
        Packets.TryAdd(29, new MovePlayerPosRotPacket());
        Packets.TryAdd(30, new MovePlayerRotPacket());
        Packets.TryAdd(37, new PlayerAbilitiesPacket());
        Packets.TryAdd(49, new SetCarriedItemPacket());
        Packets.TryAdd(58, new UseItemOnPacket());
        Packets.TryAdd(59, new UseItemPacket());
    }

    public async Task HandleConfigurationPackets(int id, byte[] data, Client client)
    {
        switch (id)
        {
            case 0:
                await HandleFromPoolAsync<ClientInformationPacket>(data, client);
                break;
            case 1:
                await HandleFromPoolAsync<CustomPayloadPacket>(data, client);
                break;
            case 3:
                await HandleFromPoolAsync<Packets.Configuration.Serverbound.FinishConfigurationPacket>(data, client);
                break;
            case 4:
                await HandleFromPoolAsync<KeepAlivePacket>(data, client);
                break;
            case 6:
                await HandleFromPoolAsync<ResourcePackPacket>(data, client);
                break;
            default:
                {
                    if (!Packets.TryGetValue(id, out var packet))
                        return;

                    try
                    {
                        using var mcStream = new MinecraftStream(data);
                        packet.Populate(mcStream);
                        await packet.HandleAsync(client.server, client.Player);
                    }
                    catch (Exception e)
                    {
                        _logger.LogCritical(e, "{exceptionMessage}", e.Message);
                    }

                    break;
                }
        }
    }

    public async Task HandlePlayPackets(int id, byte[] data, Client client)
    {
        switch (id)
        {
            case 0x00:
                await HandleFromPoolAsync<AcceptTeleportationPacket>(data, client);
                break;
            case 5:
                await HandleFromPoolAsync<ChatCommandPacket>(data, client);
                break;
            case 7:
                await HandleFromPoolAsync<ChatPacket>(data, client);
                break;
            case 8:
                await HandleFromPoolAsync<ChatSessionUpdatePacket>(data, client);
                break;
            case 9:
                await HandleFromPoolAsync<ChunkBatchReceivedPacket>(data, client);
                break;
            case 10:
                await HandleFromPoolAsync<ClientCommandPacket>(data, client);
                break;
            case 12:
                await HandleFromPoolAsync<ClientInformationPacket>(data, client);
                break;
            case 14:
                await HandleFromPoolAsync<ConfigurationAcknowledgedPacket>(data, client);
                break;
            case 15:
                await HandleFromPoolAsync<ContainerButtonClickPacket>(data, client);
                break;
            case 16:
                await HandleFromPoolAsync<ContainerClickPacket>(data, client);
                break;
            case 17:
                await HandleFromPoolAsync<ContainerClosePacket>(data, client);
                break;
            case 20:
                await HandleFromPoolAsync<CustomPayloadPacket>(data, client);
                break;
            case 24:
                await HandleFromPoolAsync<InteractPacket>(data, client);
                break;
            case 26:
                await HandleFromPoolAsync<KeepAlivePacket>(data, client);
                break;
            case 34:
                await HandleFromPoolAsync<PickItemPacket>(data, client);
                break;
            case 36:
                await HandleFromPoolAsync<PlaceRecipePacket>(data, client);
                break;
            case 38:
                await HandleFromPoolAsync<PlayerActionPacket>(data, client);
                break;
            case 39:
                await HandleFromPoolAsync<PlayerCommandPacket>(data, client);
                break;
            case 43:
                await HandleFromPoolAsync<RecipeBookSeenRecipePacket>(data, client);
                break;
            case 44:
                await HandleFromPoolAsync<RenameItemPacket>(data, client);
                break;
            case 52:
                await HandleFromPoolAsync<SetCreativeModeSlotPacket>(data, client);
                break;
            case 56:
                await HandleFromPoolAsync<SwingPacket>(data, client);
                break;
            case 58:
                await HandleFromPoolAsync<UseItemOnPacket>(data, client);
                break;
            case 59:
                await HandleFromPoolAsync<UseItemPacket>(data, client);
                break;
            default:
                if (!Packets.TryGetValue(id, out var packet))
                    return;

                try
                {
                    using var mcStream = new MinecraftStream(data);
                    packet.Populate(mcStream);
                    await packet.HandleAsync(client.server, client.Player!);
                }
                catch (Exception e)
                {
                    _logger.LogCritical(e, "{exceptionMessage}", e.Message);
                }
                break;
        }
    }

    private async Task HandleFromPoolAsync<T>(byte[] data, Client client) where T : IServerboundPacket, new()
    {
        var packet = ObjectPool<T>.Shared.Rent();
        try
        {
            using var mcStream = new MinecraftStream(data);
            packet.Populate(mcStream);
            await packet.HandleAsync(client.server, client.Player!);
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "{exceptionMessage}", e.Message);
        }
        ObjectPool<T>.Shared.Return(packet);
    }
}
