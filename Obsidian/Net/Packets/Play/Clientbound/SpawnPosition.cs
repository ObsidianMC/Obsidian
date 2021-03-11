using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SpawnPosition : ISerializablePacket
    {
        [Field(0)]
        public VectorF Position { get; }

        public int Id => 0x42;

        public SpawnPosition(VectorF position)
        {
            Position = position;
        }

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}