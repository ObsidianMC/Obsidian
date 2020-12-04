using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public partial class Disconnect : IPacket
    {
        [Field(0)]
        private ChatMessage Reason { get; set; }

        public int Id { get; }

        public byte[] Data { get; set; }

        private Disconnect()
        {
        }

        public Disconnect(ChatMessage reason, ClientState state)
        {
            this.Id = state == ClientState.Play ? 0x19 : 0x00;
            this.Reason = reason;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}