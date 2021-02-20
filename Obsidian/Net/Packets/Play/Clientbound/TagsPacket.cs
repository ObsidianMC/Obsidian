using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
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

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }

    public class Tag
    {
        public string Type { get; set; }

        public string Name { get; set; }

        public bool Replace { get; set; }

        public int Count => Entries.Count;

        public List<int> Entries { get; set; } = new();
    }
}
