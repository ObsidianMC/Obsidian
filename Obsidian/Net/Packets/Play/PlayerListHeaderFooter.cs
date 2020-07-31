using Obsidian.Chat;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerListHeaderFooter : Packet
    {
        [PacketOrder(0)]
        public ChatMessage Header { get; }

        [PacketOrder(1)]
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