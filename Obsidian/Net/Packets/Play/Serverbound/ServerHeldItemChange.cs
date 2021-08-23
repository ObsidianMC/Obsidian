using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class ServerHeldItemChange : IServerboundPacket
    {
        [Field(0)]
        public short Slot { get; private set; }

        public int Id => 0x25;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            player.CurrentSlot = Slot;

            var heldItem = player.GetHeldItem();

            await server.BroadcastPacketAsync(new EntityEquipment
            {
                EntityId = player.EntityId,
                Slot = ESlot.MainHand,
                Item = new ItemStack(heldItem.Type, heldItem.Count, heldItem.ItemMeta)
                {
                    Present = heldItem.Present
                }
            },
            excluded: player);
        }
    }
}
