using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class BlockAction : IPacket
    {
        [Field(0)]
        public Position Location { get; set; }

        [Field(1)]
        public byte ActionId { get; set; }

        [Field(2)]
        public byte ActionParam { get; set; }

        [Field(3, Type = DataType.VarInt)]
        public int BlockType { get; set; }

        public int Id => 0x0A;


        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
