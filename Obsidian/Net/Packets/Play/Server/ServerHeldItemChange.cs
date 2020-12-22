using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public partial class ServerHeldItemChange : IPacket
    {
        [Field(0)]
        public short Slot { get; set; }

        public int Id => 0x25;

        public ServerHeldItemChange() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Slot = await stream.ReadShortAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            player.CurrentSlot = (short)(this.Slot + 36);

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
                    Nbt = heldItem.Nbt
                }
            }, player);
        }
    }
}