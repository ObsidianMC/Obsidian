using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TabListPacket
{
    [Field(0)]
    public ChatMessage Header { get; }

    [Field(1)]
    public ChatMessage Footer { get; }

    public TabListPacket(ChatMessage? header, ChatMessage? footer)
    {
        ChatMessage? empty = null;

        Header = header ?? (empty ??= ChatMessage.Empty);
        Footer = footer ?? (empty ?? ChatMessage.Empty);
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(this.Header);
        writer.WriteChat(this.Footer);
    }
}
