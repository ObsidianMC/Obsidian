using System;
using System.IO;
using System.Threading.Tasks;
using Obsidian.Packets;

namespace Obsidian.Packets.Status
{
    public class PingPong
    {
        public long Payload;

        public PingPong(long payload)
        {
            this.Payload = payload;
        }

        public static async Task<PingPong> FromArrayAsync(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data))
            {
                return new PingPong(await stream.ReadLongAsync());
            }
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                await stream.WriteLongAsync(this.Payload);

                return stream.ToArray();
            }
        }
    }
}