using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class SetCarriedItemPacket
{
    [Field(0)]
    public short Slot { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Slot = reader.ReadShort();
    }

    public override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        if (Slot is < 0 or > 8) return ValueTask.CompletedTask;
        if (player is Obsidian.Entities.Player concrete) { concrete.CancelWeaponUse(); concrete.CancelEating(); }
        player.CurrentHeldItemSlot = Slot;

        var heldItem = player.GetHeldItem();

        player.Level.PacketBroadcaster.QueuePacketToLevel(player.Level, new SetEquipmentPacket
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
