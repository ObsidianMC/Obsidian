using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO finish this
public partial class PlayerChatMessagePacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Message { get; }

    [Field(1), ActualType(typeof(int)), VarLength]
    public MessageType Type { get; }

    [Field(2)]
    public Guid Sender { get; }

    [Field(3)]
    public ChatMessage DisplayName { get; }

    [Field(4)]
    public bool HasTeamDisplayName { get; }

    [Field(5), Condition("HasTeamDisplayName")]
    public ChatMessage TeamDisplayName { get; }

    [Field(6)]
    public long Timestamp { get; }

    [Field(7)]
    public long Salt { get; }

    [Field(8), VarLength]
    public int SignatureLength { get; }

    [Field(9)]
    public byte[] MessageSignature { get; }

    public int Id => 0x30;

    public PlayerChatMessagePacket(ChatMessage message, MessageType type) : this(message, type, Guid.Empty)
    {
    }

    public PlayerChatMessagePacket(ChatMessage message, MessageType type, Guid sender)
    {
        Message = message;
        Type = type;
        Sender = sender;
    }
}
