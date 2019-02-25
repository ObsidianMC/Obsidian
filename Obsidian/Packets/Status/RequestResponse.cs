using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets.Status
{
    public class RequestResponse : Packet
    {
        public RequestResponse(string json) : base(0x00, new byte[0]) => this.Json = json;

        public string Json;

        public override async Task Populate()
        {
            await Task.Yield();
            using(var stream = new MemoryStream(this._packetData))
            {

            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MemoryStream())
            {
                await stream.WriteStringAsync(this.Json);

                return stream.ToArray();
            }
        }
    }
}