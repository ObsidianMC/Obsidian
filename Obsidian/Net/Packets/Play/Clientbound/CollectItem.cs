using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class CollectItem : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int CollectedEntityId { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public int CollectorEntityId { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public int PickupItemCount { get; set; }

        public int Id => 0x55;

        public CollectItem() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
