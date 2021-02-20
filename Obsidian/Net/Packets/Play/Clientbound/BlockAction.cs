using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class BlockAction : IPacket
    {
        [Field(0)]
        public Position Position { get; set; }

        [Field(1)]
        public byte ActionId { get; set; }

        [Field(2)]
        public byte ActionParam { get; set; }

        [Field(3), VarLength]
        public int BlockType { get; set; }

        public int Id => 0x0A;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
