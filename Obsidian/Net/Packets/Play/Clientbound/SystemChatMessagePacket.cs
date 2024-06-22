using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SystemChatMessagePacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Message { get; }

    [Field(1)]
    public bool Overlay { get; }

    public int Id => 0x6C;

    public SystemChatMessagePacket(ChatMessage message, bool overlay)
    {
        this.Message = message;
        this.Overlay = overlay;
    }
}
