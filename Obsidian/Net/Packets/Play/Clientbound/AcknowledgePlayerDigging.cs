using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class AcknowledgePlayerDigging : ISerializablePacket
    {
        [Field(0)]
        public Position Position { get; set; }

        [Field(1), VarLength]
        public int Block { get; set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public DiggingStatus Status { get; set; }

        [Field(3)]
        public bool Successful { get; set; }

        public int Id => 0x07;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
