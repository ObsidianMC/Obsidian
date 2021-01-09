using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class IncomingChatMessage : IPacket
    {
        [Field(0)]
        public string Message { get; private set; }

        public int Id => 0x03;

        public IncomingChatMessage()
        {
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Message = await stream.ReadStringAsync();
        }

        public Task HandleAsync(Server server, Player player) =>
            server.ParseMessageAsync(this.Message, player.client);
    }
}