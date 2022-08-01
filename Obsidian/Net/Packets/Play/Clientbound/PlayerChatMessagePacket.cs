using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerChatMessagePacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage SignedMessage { get; }

    [Field(1)]
    public bool HasUnsignedChatContent => this.UnsignedChatMessage != null;

    [Field(2), Condition("HasUnsignedChatContent")]
    public ChatMessage UnsignedChatMessage { get; init; }

    [Field(3), ActualType(typeof(int)), VarLength]
    public MessageType Type { get; }

    [Field(4)]
    public Guid Sender { get; }

    [Field(5)]
    public ChatMessage SenderDisplayName { get; init; } = string.Empty;

    [Field(6)]
    public bool HasTeamDisplayName => this.TeamDisplayName != null;

    [Field(7), Condition("HasTeamDisplayName")]
    public ChatMessage TeamDisplayName { get; }

    [Field(8)]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [Field(9)]
    public long Salt { get; init; }

    [Field(10), VarLength]
    public int SignatureLength => this.MessageSignature.Length;

    [Field(11)]
    public byte[] MessageSignature { get; init; }

    public int Id => 0x30;

    public PlayerChatMessagePacket(ChatMessage message, MessageType type) : this(message, type, Guid.Empty)
    {
    }

    public PlayerChatMessagePacket(ChatMessage message, MessageType type, Guid? sender)
    {
        SignedMessage = message;
        Type = type;
        Sender = sender ?? Guid.Empty;
    }
}
