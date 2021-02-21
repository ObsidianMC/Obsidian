using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class CollectItem : ISerializablePacket
    {
        [Field(0), VarLength]
        public int CollectedEntityId { get; set; }

        [Field(1), VarLength]
        public int CollectorEntityId { get; set; }

        [Field(2), VarLength]
        public int PickupItemCount { get; set; }

        public int Id => 0x55;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
