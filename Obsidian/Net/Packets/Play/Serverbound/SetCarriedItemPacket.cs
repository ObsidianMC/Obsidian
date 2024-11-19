using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class SetCarriedItemPacket
{
    [Field(0)]
    public short Slot { get; private set; }

    public static SetCarriedItemPacket Deserialize(MinecraftStream stream)
    {
        var packet = new SetCarriedItemPacket();
        packet.Populate(stream);
        return packet;
    }

    public override void Populate(MinecraftStream stream) { }

    public override ValueTask HandleAsync(Server server, Player player)
    {
        player.CurrentSlot = Slot;

        var heldItem = player.GetHeldItem();

        player.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEquipmentPacket
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
        }, player.EntityId);

        return ValueTask.CompletedTask;
    }
}
