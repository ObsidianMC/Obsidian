using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    [ClientOnly]
    public partial class Disconnect : ISerializablePacket
    {
        [Field(0)]
        private ChatMessage Reason { get; }

        public int Id { get; }

        public Disconnect(ChatMessage reason, ClientState state)
        {
            Id = state == ClientState.Play ? 0x19 : 0x00;
            Reason = reason;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}