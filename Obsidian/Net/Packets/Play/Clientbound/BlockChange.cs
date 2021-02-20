using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class BlockChange : IPacket
    {
        [Field(0)]
        public Position Position { get; private set; }

        [Field(1), VarLength]
        public int BlockId { get; private set; }

        public int Id => 0x0B;

        public BlockChange(Position position, int block)
        {
            Position = position;
            BlockId = block;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}