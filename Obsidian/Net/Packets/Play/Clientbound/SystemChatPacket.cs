using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SystemChatPacket(ChatMessage message, bool overlay)
{
    [Field(0)]
    public ChatMessage Message { get; } = message;

    [Field(1)]
    public bool Overlay { get; } = overlay;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(this.Message);
        writer.WriteBoolean(this.Overlay);
    }
}
