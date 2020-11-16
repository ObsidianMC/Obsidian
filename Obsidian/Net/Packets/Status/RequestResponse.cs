using Newtonsoft.Json;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Status
{
    public partial class RequestResponse : IPacket
    {
        [Field(0)]
        public string Json;

        public int Id => 0x00;

        public RequestResponse(string json) => this.Json = json;

        public RequestResponse(ServerStatus status) => this.Json = JsonConvert.SerializeObject(status);

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}