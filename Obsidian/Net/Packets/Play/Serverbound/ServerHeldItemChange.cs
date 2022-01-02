using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ServerHeldItemChange : IServerboundPacket
{
    [Field(0)]
    public short Slot { get; private set; }

    public int Id => 0x25;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        player.CurrentSlot = Slot;

        var heldItem = player.GetHeldItem();

        await server.QueueBroadcastPacketAsync(new EntityEquipment
        {
            EntityId = player.EntityId,
            Slot = ESlot.MainHand,
            Item = heldItem
        },
        excluded: player);
    }
}
