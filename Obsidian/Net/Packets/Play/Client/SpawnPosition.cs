using Obsidian.Serializer.Attributes;
using Obsidian.API;
using System.Threading.Tasks;
using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class SpawnPosition : IPacket
    {
        [Field(0)]
        public Position Location { get; private set; }

        public int Id => 0x42;

        public byte[] Data { get; }

        public SpawnPosition(Position location) => this.Location = location;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}