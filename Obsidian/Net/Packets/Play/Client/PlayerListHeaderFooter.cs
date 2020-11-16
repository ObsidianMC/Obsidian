using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class PlayerListHeaderFooter : IPacket
    {
        [Field(0)]
        public ChatMessage Header { get; }

        [Field(1)]
        public ChatMessage Footer { get; }

        public int Id { get; } = 0x53;
        public byte[] Data { get; set; }

        public PlayerListHeaderFooter(ChatMessage header, ChatMessage footer)
        {
            var empty = new ChatMessage()
            {
                Text = ""
            };

            this.Header = header ?? empty;
            this.Footer = footer ?? empty;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}