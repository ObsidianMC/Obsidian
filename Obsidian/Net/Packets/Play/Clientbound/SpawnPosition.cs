using Obsidian.Serialization.Attributes;
using Obsidian.API;
using System.Threading.Tasks;
using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnPosition : IPacket
    {
        [Field(0)]
        public PositionF Position { get; private set; }

        public int Id => 0x42;

        private SpawnPosition()
        {
        }

        public SpawnPosition(PositionF location) => this.Position = location;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}