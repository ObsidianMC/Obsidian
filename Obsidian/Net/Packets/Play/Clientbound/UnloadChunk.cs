using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class UnloadChunk : IPacket
    {
        [Field(0)]
        public int X { get; private set; }

        [Field(1)]
        public int Z { get; private set; }

        public int Id => 0x1C;

        public byte[] Data { get; }

        private UnloadChunk()
        {
        }

        public UnloadChunk(byte[] data)
        {
            this.Data = data;
        }

        public UnloadChunk(int x, int z)
        {
            this.X = x;
            this.Z = z;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
