using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.API.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Configuration;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Utilities.Collections;

namespace Obsidian.Net;

public sealed class ClientHandler
{
    private ConcurrentDictionary<int, IServerboundPacket> Packets { get; } = new ConcurrentDictionary<int, IServerboundPacket>();
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
        // ! == moved to HandlePlayPackets
        //Packets.TryAdd(0x00, new TeleportConfirm()); !
        //Packets.TryAdd(0x01, QueryBlockNBT);
        //Packets.TryAdd(0x02, SetDifficulty);
        //Packets.TryAdd(0x03, new IncomingChatMessage()); !
        //Packets.TryAdd(0x04, ClientStatus);
        //Packets.TryAdd(0x05, new ClientSettings()); !
        //Packets.TryAdd(0x06, TabComplete);
        //Packets.TryAdd(0x07, new WindowConfirmation()); !
        //Packets.TryAdd(0x08, new ClickWindowButton()); !
        //Packets.TryAdd(0x09, new ClickWindow()); !
        //Packets.TryAdd(0x0A, new CloseWindow()); !
        //Packets.TryAdd(0x0B, new PluginMessage()); !
        //Packets.TryAdd(0x0C, EditBook);
        //Packets.TryAdd(0x0E, InteractEntity);
        //Packets.TryAdd(0x0F, GenerateStructure);
        //Packets.TryAdd(0x11, LockDifficulty);
        Packets.TryAdd(0x1A, new SetPlayerPositionPacket());
        Packets.TryAdd(0x1B, new SetPlayerPositionAndRotationPacket());
        Packets.TryAdd(0x1C, new SetPlayerRotationPacket());
        Packets.TryAdd(0x20, new PlayerAbilitiesPacket(false));
        //Packets.TryAdd(0x15, PlayerMovement);
        //Packets.TryAdd(0x16, VehicleMove);
        //Packets.TryAdd(0x17, SteerBoat);
        //Packets.TryAdd(0x18, new PickItem()); !
        //Packets.TryAdd(0x19, new CraftRecipeRequest()); !
        //Packets.TryAdd(0x1A, PlayerAbilities);
        //Packets.TryAdd(0x1B, new PlayerDigging()); !
        //Packets.TryAdd(0x1C, new EntityAction()); !
        //Packets.TryAdd(0x1D, SteerVehicle);
        //Packets.TryAdd(0x1E, new SetDisplayedRecipe()); !
        //Packets.TryAdd(0x1F, SetRecipeBookState);
        //Packets.TryAdd(0x20, new NameItem()); !
        //Packets.TryAdd(0x21, ResourcePackStatus);
        //Packets.TryAdd(0x22, AdvancementTab);
        //Packets.TryAdd(0x23, SelectTrade);
        //Packets.TryAdd(0x24, SetBeaconEffect);
        Packets.TryAdd(0x2F, new SetHeldItemPacket(false));
        //Packets.TryAdd(0x26, UpdateCommandBlock);
        //Packets.TryAdd(0x27, UpdateCommandBlockMinecart);
        //Packets.TryAdd(0x28, new CreativeInventoryAction()); !
        //Packets.TryAdd(0x29, UpdateJigsawBlock);
        //Packets.TryAdd(0x2A, UpdateStructureBlock);
        //Packets.TryAdd(0x2B, UpdateSign);
        //Packets.TryAdd(0x2C, new Animation());
        //Packets.TryAdd(0x2D, Spectate);
        //Packets.TryAdd(0x2E, new PlayerBlockPlacement()); !
        Packets.TryAdd(0x38, new UseItemOnPacket());
        Packets.TryAdd(0x39, new UseItemPacket());
    }

    public async Task HandleConfigurationPackets(int id, byte[] data, Client client)
    {
        switch (id)
        {
            case 0x00:
                await HandleFromPoolAsync<ClientInformationPacket>(data, client);
                break;
            case 0x01://Cookies
                break;
            case 0x02:
                await HandleFromPoolAsync<PluginMessagePacket>(data, client);
                break;
            case 0x03:
                await HandleFromPoolAsync<FinishConfigurationPacket>(data, client);
                break;
            case 0x04:
                await HandleFromPoolAsync<KeepAlivePacket>(data, client);
                break;
            case 0x05://pong useless
                break;
            case 0x06:
                await HandleFromPoolAsync<ResourcePackResponse>(data, client);
                break;
            default:
                {
                    if (!Packets.TryGetValue(id, out var packet))
                        return;

                    try
                    {
                        packet.Populate(data);
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
                await HandleFromPoolAsync<ConfirmTeleportationPacket>(data, client);
                break;
            case 0x04:
                await HandleFromPoolAsync<ChatCommandPacket>(data, client);
                break;
            case 0x06:
                await HandleFromPoolAsync<ChatMessagePacket>(data, client);
                break;
            case 0x07:
                await HandleFromPoolAsync<PlayerSessionPacket>(data, client);
                break;
            case 0x08:
                await HandleFromPoolAsync<ChunkBatchReceivedPacket>(data, client);
                break;
            case 0x09:
                await HandleFromPoolAsync<ClientStatusPacket>(data, client);
                break;
            case 0x0A:
                await HandleFromPoolAsync<ClientInformationPacket>(data, client);
                break;
            case 0x0C:
                await HandleFromPoolAsync<AcknowledgeConfiguration>(data, client);
                break;
            case 0x0D:
                await HandleFromPoolAsync<ClickContainerButtonPacket>(data, client);
                break;
            case 0x0E:
                await HandleFromPoolAsync<ClickContainerPacket>(data, client);
                break;
            case 0x0F:
                await HandleFromPoolAsync<CloseContainerPacket>(data, client);
                break;
            case 0x12:
                await HandleFromPoolAsync<PluginMessagePacket>(data, client);
                break;
            case 0x16:
                await HandleFromPoolAsync<InteractPacket>(data, client);
                break;

            case 0x18:
                await HandleFromPoolAsync<KeepAlivePacket>(data, client);
                break;

            case 0x20:
                await HandleFromPoolAsync<PickItemPacket>(data, client);
                break;

            case 0x22:
                await HandleFromPoolAsync<PlaceRecipePacket>(data, client);
                break;

            case 0x24:
                await HandleFromPoolAsync<PlayerActionPacket>(data, client);
                break;

            case 0x25:
                await HandleFromPoolAsync<PlayerCommandPacket>(data, client);
                break;

            case 0x29:
                await HandleFromPoolAsync<SetSeenRecipePacket>(data, client);
                break;

            case 0x2A:
                await HandleFromPoolAsync<RenameItemPacket>(data, client);
                break;

            case 0x32:
                await HandleFromPoolAsync<SetCreativeModeSlotPacket>(data, client);
                break;

            case 0x36:
                await HandleFromPoolAsync<SwingArmPacket>(data, client);
                break;

            case 0x38:
                await HandleFromPoolAsync<UseItemOnPacket>(data, client);
                break;
            case 0x39:
                await HandleFromPoolAsync<UseItemPacket>(data, client);
                break;
            default:
                if (!Packets.TryGetValue(id, out var packet))
                    return;

                try
                {
                    packet.Populate(data);
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
            packet.Populate(data);
            await packet.HandleAsync(client.server, client.Player!);
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "{exceptionMessage}", e.Message);
        }
        ObjectPool<T>.Shared.Return(packet);
    }
}
