using Newtonsoft.Json;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Status
{
    public partial class RequestResponse : IClientboundPacket
    {
        [Field(0)]
        public string Json;

        public int Id => 0x00;

        public RequestResponse()
        {
        }

        public RequestResponse(string json)
        {
            Json = json;
        }

        public RequestResponse(ServerStatus status)
        {
            Json = JsonConvert.SerializeObject(status);
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}