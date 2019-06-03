using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Packets.Status
{
    public class PingPong : Packet
    {
        public long Payload;

        public PingPong(byte[] data) : base(0x01, data){}

        protected override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this._packetData))
            {
                this.Payload = await stream.ReadLongAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteLongAsync(this.Payload);

                return stream.ToArray();
            }
        }
    }
}