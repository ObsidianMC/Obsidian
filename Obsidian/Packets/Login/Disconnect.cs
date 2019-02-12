using Obsidian.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class Disconnect
    {
        public Disconnect(Chat reason) => this.Reason = reason;

        Chat Reason;

        public static async Task<Disconnect> FromArrayAsync(byte[] data)
        {
            await Task.Yield(); throw new NotImplementedException();
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                await stream.WriteChatAsync(this.Reason);
                return stream.ToArray();
            }
        }
    }
}