using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public partial class SetCompression : IPacket
    {
        [Field(0)]
        public int Threshold { get; }

        public bool Enabled => Threshold < 0;

        public int Id => 0x03;

        public SetCompression(int threshold)
        {
            this.Threshold = threshold;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
