using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class ServerHeldItemChange : IPacket
    {
        [Field(0)]
        public short Slot { get; set; }

        public int Id => 0x25;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Slot = await stream.ReadShortAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            player.CurrentSlot = (short)(this.Slot + 36);

            var heldItem = player.GetHeldItem();

            await server.BroadcastPacketAsync(new EntityEquipment
            {
                EntityId = player.EntityId,
                Slot = ESlot.MainHand,
                Item = new ItemStack(heldItem.Type, heldItem.Count, heldItem.ItemMeta)
                {
                    Present = heldItem.Present
                }
            }, player); ;
        }
    }
}