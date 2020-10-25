using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Client
{
    public class TagsPacket : Packet
    {
        [Field(0, Type = DataType.Array)]
        public List<Tag> Blocks { get; set; }

        [Field(1, Type = DataType.Array)]
        public List<Tag> Items { get; set; }

        [Field(2, Type = DataType.Array)]
        public List<Tag> Fluid { get; set; }

        [Field(3, Type = DataType.Array)]
        public List<Tag> Entities { get; set; }

        public TagsPacket() : base(0x5B) { }
    }

    public class Tag
    {
        public string Name { get; set; }

        public int Count { get; set; }

        public List<int> Entries { get; set; }
    }
}
