using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SystemChatPacket
{
    [Field(0)]
    public ChatMessage Message { get; }

    [Field(1)]
    public bool Overlay { get; }

    public SystemChatPacket(ChatMessage message, bool overlay)
    {
        this.Message = message;
        this.Overlay = overlay;
    }
}
