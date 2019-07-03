using Newtonsoft.Json;
using Obsidian.Entities;
using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class RequestResponse : Packet
    {
        public RequestResponse(string json) : base(0x00, new byte[0]) => this.Json = json;

        public RequestResponse(ServerStatus status) : base(0x00, new byte[0]) => this.Json = JsonConvert.SerializeObject(status);

        [Variable(VariableType.String)]
        public string Json;
    }
}