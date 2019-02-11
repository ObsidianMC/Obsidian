using System;
using System.IO;
using System.Threading.Tasks;
using Obsidian.Packets;

namespace Obsidian.Packets.Status
{
    public class PingPong
    {
        public ulong Payload;

        public PingPong(ulong payload)
        {
            this.Payload = payload;
        }

        public static async Task<PingPong> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new PingPong(await stream.ReadLongAsync());
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteLongAsync(this.Payload);

            return stream.ToArray();
        }
    }
}