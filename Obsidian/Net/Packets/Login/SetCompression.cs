using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    [ClientOnly]
    public partial class SetCompression : ISerializablePacket
    {
        [Field(0)]
        public int Threshold { get; }

        public bool Enabled => Threshold < 0;

        public int Id => 0x03;

        public SetCompression(int threshold)
        {
            Threshold = threshold;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
