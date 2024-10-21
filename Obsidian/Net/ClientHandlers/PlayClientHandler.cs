using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using System.Collections.Frozen;

namespace Obsidian.Net.ClientHandlers;
internal sealed class PlayClientHandler : ClientHandler
{
    private FrozenDictionary<int, IServerboundPacket> Packets { get; } = new Dictionary<int, IServerboundPacket>()
    {
        { 0x1A, new SetPlayerPositionPacket() },
        { 0x1B, new SetPlayerPositionAndRotationPacket() },
        { 0x1C, new SetPlayerRotationPacket() },
        { 0x20, new PlayerAbilitiesPacket(false) },
        { 0x2F, new SetHeldItemPacket(false) },
        { 0x38, new UseItemOnPacket() },
        { 0x39, new UseItemPacket() }
    }.ToFrozenDictionary();

    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;
        switch (id)
        {
            case 0x00:
                return await HandleFromPoolAsync<ConfirmTeleportationPacket>(data);
            case 0x04:
                return await HandleFromPoolAsync<ChatCommandPacket>(data);
            case 0x06:
                return await HandleFromPoolAsync<ChatMessagePacket>(data);
            case 0x07:
                return await HandleFromPoolAsync<PlayerSessionPacket>(data);
            case 0x08:
                return await HandleFromPoolAsync<ChunkBatchReceivedPacket>(data);
            case 0x09:
                return await HandleFromPoolAsync<ClientStatusPacket>(data);
            case 0x0A:
                return await HandleFromPoolAsync<ClientInformationPacket>(data);
            case 0x0C:
                return await HandleFromPoolAsync<AcknowledgeConfiguration>(data);
            case 0x0D:
                return await HandleFromPoolAsync<ClickContainerButtonPacket>(data);
            case 0x0E:
                return await HandleFromPoolAsync<ClickContainerPacket>(data);
            case 0x0F:
                return await HandleFromPoolAsync<CloseContainerPacket>(data);
            case 0x12:
                return await HandleFromPoolAsync<PluginMessagePacket>(data);
            case 0x16:
                return await HandleFromPoolAsync<InteractPacket>(data);
            case 0x18:
                return await HandleFromPoolAsync<KeepAlivePacket>(data);
            case 0x20:
                return await HandleFromPoolAsync<PickItemPacket>(data);
            case 0x22:
                return await HandleFromPoolAsync<PlaceRecipePacket>(data);
            case 0x24:
                return await HandleFromPoolAsync<PlayerActionPacket>(data);
            case 0x25:
                return await HandleFromPoolAsync<PlayerCommandPacket>(data);
            case 0x29:
                return await HandleFromPoolAsync<SetSeenRecipePacket>(data);
            case 0x2A:
                return await HandleFromPoolAsync<RenameItemPacket>(data);
            case 0x32:
                return await HandleFromPoolAsync<SetCreativeModeSlotPacket>(data);
            case 0x36:
                return await HandleFromPoolAsync<SwingArmPacket>(data);
            case 0x38:
                return await HandleFromPoolAsync<UseItemOnPacket>(data);
            case 0x39:
                return await HandleFromPoolAsync<UseItemPacket>(data);
            default:
                if (!Packets.TryGetValue(id, out var packet))
                    return false;

                try
                {
                    packet.Populate(data);
                    await packet.HandleAsync(this.Server, this.Client.Player!);
                }
                catch (Exception e)
                {
                    this.Logger.LogCritical(e, "An error has occured trying to populate a packet.");
                }
                break;
        }

        return false;
    }
}
