using Obsidian.API;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class AcknowledgePlayerDigging : IClientboundPacket
    {
        [Field(0)]
        public Vector Position { get; set; }

        [Field(1), VarLength]
        public int Block { get; set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public DiggingStatus Status { get; set; }

        [Field(3)]
        public bool Successful { get; set; }

        public int Id => 0x07;
    }
}
