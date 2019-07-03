using Obsidian.Util;

namespace Obsidian.Net.Packets.Play
{
    public class EntityPacket : Packet
    {
        [Variable]
        public int Id { get; set; }

        public EntityPacket() : base(0x27, new byte[0]) { }
    }
}
