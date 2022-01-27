using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChatMessagePacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Message { get; }

    [Field(1), ActualType(typeof(sbyte))]
    public MessageType Type { get; }

    [Field(2)]
    public Guid Sender { get; }

    public int Id => 0x0F;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public ChatMessagePacket(ChatMessage message, MessageType type) : this(message, type, Guid.Empty)
    {
    }

    public ChatMessagePacket(ChatMessage message, MessageType type, Guid sender)
    {
        Message = message;
        Type = type;
        Sender = sender;
    }
}
