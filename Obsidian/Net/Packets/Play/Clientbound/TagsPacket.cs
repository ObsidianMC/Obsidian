using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class TagsPacket : IClientboundPacket
    {
        [Field(0)]
        public List<Tag> Blocks { get; init; }

        [Field(1)]
        public List<Tag> Items { get; init; }

        [Field(2)]
        public List<Tag> Fluids { get; init; }

        [Field(3)]
        public List<Tag> Entities { get; init; }

        public int Id => 0x66;
    }

    public class Tag
    {
        public string Name { get; init; }
        public string Type { get; init; }
        public bool Replace { get; init; }
        public List<int> Entries { get; init; } = new();
        public int Count => Entries.Count;
    }

    public class RawTag
    {
        public string Name { get; init; }
        public string Type { get; init; }
        public bool Replace { get; init; }
        public List<string> Values { get; set; }
    }
}
