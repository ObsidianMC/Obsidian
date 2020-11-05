using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class Disconnect : IPacket
    {
        [Field(0)]
        private readonly ChatMessage reason;

        public int Id { get; }

        public byte[] Data { get; set; }

        public Disconnect(ChatMessage reason, ClientState state)
        {
            this.Id = state == ClientState.Play ? 0x19 : 0x00;
            this.reason = reason;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}