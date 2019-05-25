using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets.Status
{
    public class PingPong : Packet
    {
        public long Payload;

        public PingPong(byte[] data) : base(0x01, data){}

        protected override async Task PopulateAsync()
        {
            using (var stream = new MemoryStream(this._packetData))
            {
                this.Payload = await stream.ReadLongAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteLongAsync(this.Payload);

                return stream.ToArray();
            }
        }
    }
}