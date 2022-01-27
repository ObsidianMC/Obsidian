using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerListHeaderFooter : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Header { get; }

    [Field(1)]
    public ChatMessage Footer { get; }

    public int Id => 0x5F;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public PlayerListHeaderFooter(ChatMessage header, ChatMessage footer)
    {
        var empty = ChatMessage.Empty;

        Header = header ?? empty;
        Footer = footer ?? empty;
    }
}
