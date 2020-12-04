using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class CollectItem : IPacket
    {
        [Field(0), VarLength]
        public int CollectedEntityId { get; set; }

        [Field(1), VarLength]
        public int CollectorEntityId { get; set; }

        [Field(2), VarLength]
        public int PickupItemCount { get; set; }

        public int Id => 0x55;

        public CollectItem() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
