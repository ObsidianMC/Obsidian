using Obsidian.Chat;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerListHeaderFooter : Packet
    {
        [Field(0)]
        public ChatMessage Header { get; }

        [Field(1)]
        public ChatMessage Footer { get; }

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
    }
}