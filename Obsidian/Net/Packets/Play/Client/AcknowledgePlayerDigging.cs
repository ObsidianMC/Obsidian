using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class AcknowledgePlayerDigging : IPacket
    {
        [Field(0)]
        public Position Location { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public int Block { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public DiggingStatus Status { get; set; }

        [Field(3)]
        public bool Successful { get; set; }

        public int Id => 0x07;

        public AcknowledgePlayerDigging() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
