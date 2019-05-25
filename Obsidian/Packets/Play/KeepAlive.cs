using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class KeepAlive : Packet
    {
        public KeepAlive(long id) : base(0x21, new byte[0])
        {
            this.KeepAliveId = id;
        }

        public KeepAlive(byte[] data) : base(0x21, data) { }

        public long KeepAliveId { get; set; }

        protected override async Task PopulateAsync()
        {
            using (var stream = new MemoryStream(this._packetData))
            {
                this.KeepAliveId = await stream.ReadLongAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteLongAsync(this.KeepAliveId);
                return stream.ToArray();
            }
        }
    }
}