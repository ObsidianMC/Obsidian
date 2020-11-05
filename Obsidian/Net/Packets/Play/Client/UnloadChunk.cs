using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class UnloadChunk : IPacket
    {
        [Field(0, true)]
        public int X { get; private set; }

        [Field(1, true)]
        public int Z { get; private set; }

        public int Id => 0x1C;

        public byte[] Data { get; }

        public UnloadChunk(byte[] data) { this.Data = data; }

        public UnloadChunk(int x, int z)
        {
            this.X = x;
            this.Z = z;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
