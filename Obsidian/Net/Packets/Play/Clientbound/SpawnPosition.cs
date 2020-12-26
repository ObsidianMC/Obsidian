using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnPosition : IPacket
    {
        [Field(0)]
        public PositionF Position { get; private set; }

        public int Id => 0x42;

        public byte[] Data { get; }

        private SpawnPosition()
        {
        }

        public SpawnPosition(PositionF position) => this.Position = position;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}