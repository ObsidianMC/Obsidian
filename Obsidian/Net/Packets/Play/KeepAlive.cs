using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class KeepAlive : Packet
    {
        [Field(0)]
        public long KeepAliveId { get; set; }

        public KeepAlive() : base(0x21) { }

        public KeepAlive(long id) : base(0x21)
        {
            this.KeepAliveId = id;
        }

        public KeepAlive(byte[] data) : base(0x21, data) { }
    }
}