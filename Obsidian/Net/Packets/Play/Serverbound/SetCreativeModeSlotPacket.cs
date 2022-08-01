using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetCreativeModeSlotPacket : IServerboundPacket
{
    [Field(0)]
    public short ClickedSlot { get; private set; }

    [Field(1)]
    public ItemStack ClickedItem { get; private set; }

    public int Id => 0x2A;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        var inventory = player.OpenedContainer ?? player.Inventory;

        var (slot, isForPlayer) = inventory.GetDifference(ClickedSlot);

        if (isForPlayer)
            inventory = player.Inventory;

        inventory.SetItem(slot, ClickedItem);

        player.LastClickedItem = ClickedItem;

        if (player.inventorySlot == ClickedSlot)
        {
            var heldItem = player.GetHeldItem();

            await server.QueueBroadcastPacketAsync(new SetEquipmentPacket
            {
                EntityId = player.EntityId,
                Equipment = new()
                {
                    new()
                    {
                        Item = heldItem,
                        Slot = API.Inventory.EquipmentSlot.MainHand
                    }
                }
            }, player);
        }
    }
}
