using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class EntityPosition : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1), DataFormat(typeof(short))]
        public Vector Delta { get; set; }

        [Field(4)]
        public bool OnGround { get; set; }

        public int Id => 0x27;
    }
}
