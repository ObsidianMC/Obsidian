using System;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets.Status
{
    public class RequestResponse
    {
        public RequestResponse() { }
        public RequestResponse(string json) => this.Json = json;

        public string Json;

        public async Task<RequestResponse> FromByteAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new RequestResponse();
        }

        public async Task<byte[]> GetData()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteStringAsync(this.Json);

            return stream.ToArray();
        }

        public async Task<MemoryStream> GetDataStream()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteStringAsync(this.Json);

            return stream;
        }
    }
}