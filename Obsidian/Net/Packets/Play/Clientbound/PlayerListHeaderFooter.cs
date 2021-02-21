using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class PlayerListHeaderFooter : ISerializablePacket
    {
        [Field(0)]
        public ChatMessage Header { get; }

        [Field(1)]
        public ChatMessage Footer { get; }

        public int Id => 0x53;

        public PlayerListHeaderFooter(ChatMessage header, ChatMessage footer)
        {
            var empty = ChatMessage.Empty;

            Header = header ?? empty;
            Footer = footer ?? empty;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}