using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class TagsPacket : IPacket
    {
        [Field(0)]
        public List<Tag> Blocks { get; set; }

        [Field(1)]
        public List<Tag> Items { get; set; }

        [Field(2)]
        public List<Tag> Fluid { get; set; }

        [Field(3)]
        public List<Tag> Entities { get; set; }

        public int Id => 0x5B;

        public TagsPacket() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }

    public class Tag
    {
        public string Name { get; set; }

        public int Count { get; set; }

        public List<int> Entries { get; set; }
    }
}
