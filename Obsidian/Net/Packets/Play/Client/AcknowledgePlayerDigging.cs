using Obsidian.API;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Client
{
    public class AcknowledgePlayerDigging : Packet
    {
        [Field(0)]
        public Position Location { get; set; }

        [Field(1, Type = DataType.VarInt)]
        public int Block { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public DiggingStatus Status { get; set; }

        [Field(3)]
        public bool Successful { get; set; }

        public AcknowledgePlayerDigging() : base(0x07) { }
    }
}
