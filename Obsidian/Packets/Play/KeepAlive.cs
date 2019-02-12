using Obsidian.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class KeepAlive
    {
        public KeepAlive(long id) => this.KeepAliveId = id;

        public long KeepAliveId { get; }

        public static async Task<KeepAlive> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new KeepAlive(await stream.ReadLongAsync());
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteLongAsync(this.KeepAliveId);
            return stream.ToArray();
        }
    }
}