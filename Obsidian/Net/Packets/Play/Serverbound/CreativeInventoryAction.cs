using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class CreativeInventoryAction : IServerboundPacket
    {
        [Field(0)]
        public short ClickedSlot { get; private set; }

        [Field(1)]
        public ItemStack ClickedItem { get; private set; }

        public int Id => 0x28;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            var inventory = player.OpenedContainer ?? player.Inventory;

            var (slot, isForPlayer) = ClickedSlot.GetDifference(inventory.Size);

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
                    Item = new ItemStack(heldItem.Type, heldItem.Count, heldItem.ItemMeta)
                    {
                        Present = heldItem.Present
                    }
                }, player);
            }
        }
    }
}
