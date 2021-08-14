using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class PlayerListHeaderFooter : IClientboundPacket
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
    }
}
