using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class CreativeInventoryAction : IServerboundPacket
{
    [Field(0)]
    public short ClickedSlot { get; private set; }

    [Field(1)]
    public ItemStack ClickedItem { get; private set; }

    public int Id => 0x28;

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

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

            await server.QueueBroadcastPacketAsync(new EntityEquipment
            {
                EntityId = player.EntityId,
                Slot = ESlot.MainHand,
                Item = heldItem
            }, player);
        }
    }
}
