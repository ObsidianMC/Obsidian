using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetTabListHeaderAndFooterPacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Header { get; }

    [Field(1)]
    public ChatMessage Footer { get; }

    public int Id => 0x6D;

    //TODO send empty text component when writing chat message for this e.x {"text":""}
    public SetTabListHeaderAndFooterPacket(ChatMessage? header, ChatMessage? footer)
    {
        ChatMessage? empty = null; // ChatMessage.Empty allocates a new ChatMessage (ChatMessage is mutable)

        Header = header ?? (empty ??= ChatMessage.Empty);
        Footer = footer ?? (empty ?? ChatMessage.Empty);
    }
}
