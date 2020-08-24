using Obsidian.Chat;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerListHeaderFooter : Packet
    {
        public PlayerListHeaderFooter(ChatMessage header, ChatMessage footer) : base(0x4E)
        {
            this.Header = header ?? new ChatMessage()
            {
                HoverEvent = new TextComponent { Translate = "" }
            };

            this.Footer = footer ?? new ChatMessage()
            {
                HoverEvent = new TextComponent { Translate = "" }
            };
        }

        public ChatMessage Header { get; }

        public ChatMessage Footer { get; }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteChatAsync(Header);
            await stream.WriteChatAsync(Footer);
        }
    }
}