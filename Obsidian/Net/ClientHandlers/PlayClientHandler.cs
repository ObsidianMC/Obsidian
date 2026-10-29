using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Serverbound;
using System.Collections.Frozen;

namespace Obsidian.Net.ClientHandlers;

internal sealed partial class PlayClientHandler : ClientHandler
{
    private FrozenDictionary<int, IServerboundPacket> Packets { get; } = new Dictionary<int, IServerboundPacket>()
    {
        { MovePlayerPosPacket.PacketId, new MovePlayerPosPacket() },
        { MovePlayerPosRotPacket.PacketId, new MovePlayerPosRotPacket() },
        { MovePlayerRotPacket.PacketId, new MovePlayerRotPacket() },
        { PlayerAbilitiesPacket.PacketId, new PlayerAbilitiesPacket() },
        { ClientTickEndPacket.PacketId, new ClientTickEndPacket() }
    }.ToFrozenDictionary();

    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;
        switch (id)
        {
            case AcceptTeleportationPacket.PacketId:
                await HandleFromPoolAsync<AcceptTeleportationPacket>(data);
                break;
            case AttackPacket.PacketId:
                await HandleFromPoolAsync<AttackPacket>(data);
                break;
            case BundleItemSelectedPacket.PacketId:
                await HandleFromPoolAsync<BundleItemSelectedPacket>(data);
                break;
            case ChatCommandPacket.PacketId:
                await HandleFromPoolAsync<ChatCommandPacket>(data);
                break;
            case ChatPacket.PacketId:
                await HandleFromPoolAsync<ChatPacket>(data);
                break;
            case ChatSessionUpdatePacket.PacketId:
                await HandleFromPoolAsync<ChatSessionUpdatePacket>(data);
                break;
            case ChunkBatchReceivedPacket.PacketId:
                await HandleFromPoolAsync<ChunkBatchReceivedPacket>(data);
                break;
            case ClientCommandPacket.PacketId:
                await HandleFromPoolAsync<ClientCommandPacket>(data);
                break;
            case ClientInformationPacket.ServerboundPlayId:
                await HandleFromPoolAsync<ClientInformationPacket>(data);
                break;
            case CommandSuggestionPacket.PacketId:
                await HandleFromPoolAsync<CommandSuggestionPacket>(data);
                break;
            case ConfigurationAcknowledgedPacket.PacketId:
                await this.Client.AcknowledgeReconfigurationAsync();
                break;
            case ContainerButtonClickPacket.PacketId:
                await HandleFromPoolAsync<ContainerButtonClickPacket>(data);
                break;
            case ContainerClickPacket.PacketId:
                await HandleFromPoolAsync<ContainerClickPacket>(data);
                break;
            case ContainerClosePacket.PacketId:
                await HandleFromPoolAsync<ContainerClosePacket>(data);
                break;
            case CustomPayloadPacket.ServerboundPlayId:
                {
                    var payload = new CustomPayloadPacket();
                    payload.Populate(data);

                    await this.Client.Channels.HandlePayloadAsync(payload.Channel, payload.PluginData);
                    break;
                }
            case InteractPacket.PacketId:
                await HandleFromPoolAsync<InteractPacket>(data);
                break;
            case KeepAlivePacket.ServerboundPlayId:
                await HandleFromPoolAsync<KeepAlivePacket>(data);
                break;
            case MoveVehiclePacket.PacketId:
                await HandleFromPoolAsync<MoveVehiclePacket>(data);
                break;
            case PickItemFromBlockPacket.PacketId:
                await HandleFromPoolAsync<PickItemFromBlockPacket>(data);
                break;
            case PlaceRecipePacket.PacketId:
                await HandleFromPoolAsync<PlaceRecipePacket>(data);
                break;
            case PlayerActionPacket.PacketId:
                await HandleFromPoolAsync<PlayerActionPacket>(data);
                break;
            case PlayerCommandPacket.PacketId:
                await HandleFromPoolAsync<PlayerCommandPacket>(data);
                break;
            case PlayerInputPacket.PacketId:
                await HandleFromPoolAsync<PlayerInputPacket>(data);
                break;
            case PlayerLoadedPacket.PacketId:
                await HandleFromPoolAsync<PlayerLoadedPacket>(data);
                break;
            case PunchPacket.PacketId:
                await HandleFromPoolAsync<PunchPacket>(data);
                break;
            case RecipeBookChangeSettingsPacket.PacketId:
                await HandleFromPoolAsync<RecipeBookChangeSettingsPacket>(data);
                break;
            case RecipeBookSeenRecipePacket.PacketId:
                await HandleFromPoolAsync<RecipeBookSeenRecipePacket>(data);
                break;
            case SelectTradePacket.PacketId:
                await HandleFromPoolAsync<SelectTradePacket>(data);
                break;
            case SetGameRulePacket.PacketId:
                await HandleFromPoolAsync<SetGameRulePacket>(data);
                break;
            case RenameItemPacket.PacketId:
                await HandleFromPoolAsync<RenameItemPacket>(data);
                break;
            case SignUpdatePacket.PacketId:
                await HandleFromPoolAsync<SignUpdatePacket>(data);
                break;
            case SetCarriedItemPacket.PacketId:
                await HandleFromPoolAsync<SetCarriedItemPacket>(data);
                break;
            case SetCreativeModeSlotPacket.PacketId:
                await HandleFromPoolAsync<SetCreativeModeSlotPacket>(data);
                break;
            case SpectatorActionPacket.PacketId:
                await HandleFromPoolAsync<SpectatorActionPacket>(data);
                break;
            case UseItemOnPacket.PacketId:
                await HandleFromPoolAsync<UseItemOnPacket>(data);
                break;
            case UseItemPacket.PacketId:
                await HandleFromPoolAsync<UseItemPacket>(data);
                break;
            case CustomClickActionPacket.PacketId:
                await HandleFromPoolAsync<CustomClickActionPacket>(data);
                break;
            default:
                if (!Packets.TryGetValue(id, out var packet))
                    return false;

                try
                {
                    packet.Populate(data);
                    await packet.HandleAsync(this.Server, this.Player!);
                }
                catch (Exception e)
                {
                    Log.PacketFailed(this.Logger, e, packet.GetType().Name);
                }
                break;
        }

        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Handling packet {PacketType} failed")]
        public static partial void PacketFailed(ILogger logger, Exception exception, string packetType);
    }
}
