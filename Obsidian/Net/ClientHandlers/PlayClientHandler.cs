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
        { 28, new MovePlayerPosPacket() },
        { 29, new MovePlayerPosRotPacket() },
        { 30, new MovePlayerRotPacket() },
        { 38, new PlayerAbilitiesPacket() },
        { 60, new UseItemOnPacket() },
        { 61, new UseItemPacket() },
        { 11, new ClientTickEndPacket() }
    }.ToFrozenDictionary();

    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;
        switch (id)
        {
            case 0:
                await HandleFromPoolAsync<AcceptTeleportationPacket>(data);
                break;
            case 5:
                await HandleFromPoolAsync<ChatCommandPacket>(data);
                break;
            case 7:
                await HandleFromPoolAsync<ChatPacket>(data);
                break;
            case 8:
                await HandleFromPoolAsync<ChatSessionUpdatePacket>(data);
                break;
            case 9:
                await HandleFromPoolAsync<ChunkBatchReceivedPacket>(data);
                break;
            case 10:
                await HandleFromPoolAsync<ClientCommandPacket>(data);
                break;
            case 12:
                await HandleFromPoolAsync<ClientInformationPacket>(data);
                break;
            case 14:
                await HandleFromPoolAsync<ConfigurationAcknowledgedPacket>(data);
                break;
            case 15:
                await HandleFromPoolAsync<ContainerButtonClickPacket>(data);
                break;
            case 16:
                await HandleFromPoolAsync<ContainerClickPacket>(data);
                break;
            case 17:
                await HandleFromPoolAsync<ContainerClosePacket>(data);
                break;
            case 20:
                await HandleFromPoolAsync<CustomPayloadPacket>(data);
                break;
            case 24:
                await HandleFromPoolAsync<InteractPacket>(data);
                break;
            case 26:
                await HandleFromPoolAsync<KeepAlivePacket>(data);
                break;
            case 34:
                await HandleFromPoolAsync<PickItemFromBlockPacket>(data);
                break;
            case 37:
                await HandleFromPoolAsync<PlaceRecipePacket>(data);
                break;
            case 39:
                await HandleFromPoolAsync<PlayerActionPacket>(data);
                break;
            case 40:
                await HandleFromPoolAsync<PlayerCommandPacket>(data);
                break;
            case 45:
                await HandleFromPoolAsync<RecipeBookSeenRecipePacket>(data);
                break;
            case 46:
                await HandleFromPoolAsync<RenameItemPacket>(data);
                break;
            case 54:
                await HandleFromPoolAsync<SetCreativeModeSlotPacket>(data);
                break;
            case 58:
                await HandleFromPoolAsync<SwingPacket>(data);
                break;
            case 60:
                await HandleFromPoolAsync<UseItemOnPacket>(data);
                break;
            case 61:
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
                    using var mcStream = new MinecraftStream(data);
                    packet.Populate(mcStream);
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
