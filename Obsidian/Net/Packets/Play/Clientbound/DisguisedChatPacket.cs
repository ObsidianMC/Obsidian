using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class DisguisedChatPacket
{
    [Field(0)]
    public ChatMessage Message { get; init; }

    [Field(1), VarLength]
    public int ChatType { get; init; }

    [Field(2)]
    public ChatMessage SenderName { get; init; }

    [Field(3)]
    public bool HasTargetName { get; init; }

    [Field(4)]
    [Condition("HasTargetName")]
    public ChatMessage TargetName { get; init; }
}
