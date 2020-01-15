using Newtonsoft.Json;
using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class RequestResponse : Packet
    {
        public RequestResponse(string json) : base(0x00, System.Array.Empty<byte>()) => this.Json = json;

        public RequestResponse(ServerStatus status) : base(0x00, System.Array.Empty<byte>()) => this.Json = JsonConvert.SerializeObject(status);

        public string Json;

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteStringAsync(this.Json);

        protected override Task PopulateAsync(MinecraftStream stream) => throw new System.NotImplementedException();
    }
}