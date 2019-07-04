using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class KeepAlive : Packet
    {
        public KeepAlive(long id) : base(0x21, new byte[0])
        {
            this.KeepAliveId = id;
        }

        public KeepAlive(byte[] data) : base(0x21, data)
        {
        }

        [Variable]
        public long KeepAliveId { get; set; }
    }
}