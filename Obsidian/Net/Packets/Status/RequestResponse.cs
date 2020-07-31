using Newtonsoft.Json;
using Obsidian.Serializer.Attributes;
using Obsidian.Util;

namespace Obsidian.Net.Packets.Status
{
    public class RequestResponse : Packet
    {
        [PacketOrder(0)]
        public string Json;

        public RequestResponse(string json) : base(0x00) => this.Json = json;

        public RequestResponse(ServerStatus status) : base(0x00) => this.Json = JsonConvert.SerializeObject(status);
    }
}