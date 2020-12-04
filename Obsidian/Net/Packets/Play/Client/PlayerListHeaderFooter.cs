using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class PlayerListHeaderFooter : IPacket
    {
        [Field(0)]
        public ChatMessage Header { get; private set; }

        [Field(1)]
        public ChatMessage Footer { get; private set; }

        public int Id { get; } = 0x53;
        public byte[] Data { get; set; }

        private PlayerListHeaderFooter()
        {
        }

        public PlayerListHeaderFooter(ChatMessage header, ChatMessage footer)
        {
            var empty = ChatMessage.Empty;

            this.Header = header ?? empty;
            this.Footer = footer ?? empty;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}