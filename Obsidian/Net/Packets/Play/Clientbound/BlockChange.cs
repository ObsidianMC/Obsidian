using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class BlockChange : IPacket
    {
        [Field(0)]
        public Position Location { get; private set; }

        [Field(1, Type = DataType.VarInt)]
        public int BlockId { get; private set; }

        public int Id => 0x0B;

        public BlockChange() { }

        public BlockChange(Position loc, int block)
        {
            Location = loc;
            BlockId = block;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}