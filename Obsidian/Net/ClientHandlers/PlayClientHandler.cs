using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Serverbound;
using System.Collections.Frozen;

namespace Obsidian.Net.ClientHandlers;
internal sealed class PlayClientHandler : ClientHandler
{
    private FrozenDictionary<int, IServerboundPacket> Packets { get; } = new Dictionary<int, IServerboundPacket>()
    {
        { 29, new MovePlayerPosPacket() },
        { 30, new MovePlayerPosRotPacket() },
        { 31, new MovePlayerRotPacket() },
        { 39, new PlayerAbilitiesPacket() },
        { 63, new UseItemOnPacket() },
        { 64, new UseItemPacket() },
        { 12, new ClientTickEndPacket() }
    }.ToFrozenDictionary();

    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;
        switch (id)
        {
            case 0:
                await HandleFromPoolAsync<AcceptTeleportationPacket>(data);
                break;
            case 6:
                await HandleFromPoolAsync<ChatCommandPacket>(data);
                break;
            case 8:
                await HandleFromPoolAsync<ChatPacket>(data);
                break;
            case 9:
                await HandleFromPoolAsync<ChatSessionUpdatePacket>(data);
                break;
            case 10:
                await HandleFromPoolAsync<ChunkBatchReceivedPacket>(data);
                break;
            case 11:
                await HandleFromPoolAsync<ClientCommandPacket>(data);
                break;
            case 13:
                await HandleFromPoolAsync<ClientInformationPacket>(data);
                break;
            case 15:
                await HandleFromPoolAsync<ConfigurationAcknowledgedPacket>(data);
                break;
            case 16:
                await HandleFromPoolAsync<ContainerButtonClickPacket>(data);
                break;
            case 17:
                await HandleFromPoolAsync<ContainerClickPacket>(data);
                break;
            case 18:
                await HandleFromPoolAsync<ContainerClosePacket>(data);
                break;
            case 21:
                await HandleFromPoolAsync<CustomPayloadPacket>(data);
                break;
            case 25:
                await HandleFromPoolAsync<InteractPacket>(data);
                break;
            case 27:
                await HandleFromPoolAsync<KeepAlivePacket>(data);
                break;
            case 35:
                await HandleFromPoolAsync<PickItemFromBlockPacket>(data);
                break;
            case 38:
                await HandleFromPoolAsync<PlaceRecipePacket>(data);
                break;
            case 40:
                await HandleFromPoolAsync<PlayerActionPacket>(data);
                break;
            case 41:
                await HandleFromPoolAsync<PlayerCommandPacket>(data);
                break;
            case 42:
                await HandleFromPoolAsync<PlayerInputPacket>(data);
                break;
            case 46:
                await HandleFromPoolAsync<RecipeBookSeenRecipePacket>(data);
                break;
            case 47:
                await HandleFromPoolAsync<RenameItemPacket>(data);
                break;
            case 52:
                await HandleFromPoolAsync<SetCarriedItemPacket>(data);
                break;
            case 55:
                await HandleFromPoolAsync<SetCreativeModeSlotPacket>(data);
                break;
            case 60:
                await HandleFromPoolAsync<SwingPacket>(data);
                break;
            case 63:
                await HandleFromPoolAsync<UseItemOnPacket>(data);
                break;
            case 64:
                await HandleFromPoolAsync<UseItemPacket>(data);
                break;
            default:
                if (!Packets.TryGetValue(id, out var packet))
                {
                    this.Client.Logger.LogTrace("Play Packet({id}) {name} is not being handled.", id, PacketsRegistry.Play.ServerboundNames[id]);
                    return false;
                }

                try
                {
                    packet.Populate(data);
                    await packet.HandleAsync(this.Server, this.Client.Player!);
                }
                catch (Exception e)
                {
                    this.Logger.LogCritical(e, "An error has occured trying to populate play packet({id}) {name}.", id, PacketsRegistry.Play.ServerboundNames[id]);
                }
                break;
        }

        return false;
    }
}
