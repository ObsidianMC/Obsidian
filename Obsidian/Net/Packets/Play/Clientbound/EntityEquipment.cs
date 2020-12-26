using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class EntityEquipment : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public ESlot Slot { get; set; }

        [Field(2)]
        public ItemStack Item { get; set; }

        public int Id => 0x47;

        public EntityEquipment() : base() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }

    public enum ESlot : int
    {
        MainHand,
        OffHand,

        Boots,
        Leggings,
        Chestplate,
        Helmet
    }
}
