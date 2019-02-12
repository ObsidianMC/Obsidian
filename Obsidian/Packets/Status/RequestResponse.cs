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
            await Task.Yield();
            using (MemoryStream stream = new MemoryStream(data))
            {
                // empty data: consider taking away stream or making a separate object.
                return new RequestResponse();
            }
        }

        public async Task<byte[]> GetDataAsync()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                await stream.WriteStringAsync(this.Json);

                return stream.ToArray();
            }
        }
    }
}