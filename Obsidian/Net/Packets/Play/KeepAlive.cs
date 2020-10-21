using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class KeepAlive : Packet
    {
        [Field(0)]
        public long KeepAliveId { get; set; }

        public KeepAlive() : base(0x1F) { }

        public KeepAlive(long id) : base(0x1F)
        {
            this.KeepAliveId = id;
        }

        public KeepAlive(byte[] data) : base(0x1F, data) { }
    }
}