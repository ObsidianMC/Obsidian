using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetHeldItemPacket : IServerboundPacket
{
    [Field(0)]
    public short Slot { get; private set; }

    public int Id => 0x47;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        player.CurrentSlot = Slot;

        var heldItem = player.GetHeldItem();

        await server.QueueBroadcastPacketAsync(new SetEquipmentPacket
        {
            EntityId = player.EntityId,
            Equipment = new()
            {
                new()
                {
                    Slot = EquipmentSlot.MainHand,
                    Item = heldItem
                }
            }
        },
        excluded: player);
    }
}
