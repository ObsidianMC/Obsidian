using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;

namespace Obsidian.Net.Packets.Play.Server
{
    public class TeleportConfirm : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int TeleportId { get; set; }

        public TeleportConfirm() : base(0x00) { }
    }
}
