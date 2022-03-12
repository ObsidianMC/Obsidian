using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerListHeaderFooter : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Header { get; }

    [Field(1)]
    public ChatMessage Footer { get; }

    public int Id => 0x5F;

    public PlayerListHeaderFooter(ChatMessage? header, ChatMessage? footer)
    {
        ChatMessage? empty = null; // ChatMessage.Empty allocates a new ChatMessage (ChatMessage is mutable)

        Header = header ?? (empty ??= ChatMessage.Empty);
        Footer = footer ?? (empty ?? ChatMessage.Empty);
    }
}
