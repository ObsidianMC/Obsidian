using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.WorldData;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class ServerDifficulty : IPacket
    {
        [Field(0, Type = DataType.UnsignedByte)]
        public Difficulty Difficulty { get; }

        public int Id => 0x0D;

        public ServerDifficulty(Difficulty difficulty) => this.Difficulty = difficulty;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}