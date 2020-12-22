using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class ServerDifficulty : IPacket
    {
        [Field(0), ActualType(typeof(byte))]
        public Difficulty Difficulty { get; private set; }

        public int Id => 0x0D;

        private ServerDifficulty()
        {
        }

        public ServerDifficulty(Difficulty difficulty) => this.Difficulty = difficulty;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}