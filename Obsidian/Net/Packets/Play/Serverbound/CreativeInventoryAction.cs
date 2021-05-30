using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class CreativeInventoryAction : IServerboundPacket
    {
        [Field(0)]
        public short ClickedSlot { get; set; }

        [Field(1)]
        public ItemStack ClickedItem { get; set; }

        public int Id => 0x29;

        public async Task HandleAsync(Server server, Player player)
        {
            var inventory = player.OpenedInventory ?? player.Inventory;

            var (value, forPlayer) = this.ClickedSlot.GetDifference(inventory.Size);

            if (forPlayer)
                inventory = player.Inventory;

            inventory.SetItem(value, this.ClickedItem);

            player.LastClickedItem = this.ClickedItem;

            if (player.CurrentSlot == this.ClickedSlot)
            {
                var heldItem = player.GetHeldItem();

                await server.BroadcastPacketAsync(new EntityEquipment
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
