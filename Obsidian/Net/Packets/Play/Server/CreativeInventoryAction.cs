using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public class CreativeInventoryAction : IPacket
    {
        [Field(0)]
        public short ClickedSlot { get; set; }

        [Field(1)]
        public ItemStack ClickedItem { get; set; }

        public int Id => 0x29;

        public CreativeInventoryAction() : base() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.ClickedSlot = await stream.ReadShortAsync();
            this.ClickedItem = await stream.ReadSlotAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            player.Inventory.SetItem(this.ClickedSlot, new ItemStack(this.ClickedItem.Id, this.ClickedItem.Count)
            {
                ItemMeta = this.ClickedItem.ItemMeta,
                Present = this.ClickedItem.Present
            });

            if (this.ClickedSlot >= 36 && this.ClickedSlot <= 44)
            {
                var heldItem = player.GetHeldItem();
                await server.BroadcastPacketAsync(new EntityEquipment
                {
                    EntityId = player.EntityId,
                    Slot = ESlot.MainHand,
                    Item = new ItemStack
                    {
                        Present = heldItem.Present,
                        Count = (sbyte)heldItem.Count,
                        Id = heldItem.Id,
                        ItemMeta = heldItem.ItemMeta
                    }
                }, player);
            }
        }
    }
}
