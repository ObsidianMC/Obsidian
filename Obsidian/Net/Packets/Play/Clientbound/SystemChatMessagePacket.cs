using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class SystemChatMessagePacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Message { get; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public MessageType Type { get; }

    public int Id => 0x62;

    public SystemChatMessagePacket(ChatMessage message, MessageType type)
    {
        this.Message = message;
        this.Type = type;
    }
}
