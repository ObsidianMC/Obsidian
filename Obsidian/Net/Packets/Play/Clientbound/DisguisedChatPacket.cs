using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class DisguisedChatPacket
{
    [Field(0)]
    public required ChatMessage Message { get; init; }

    [Field(1), VarLength]
    public required int ChatType { get; init; }

    [Field(2)]
    public required ChatMessage SenderName { get; init; }

    [Field(4)]
    [Condition("HasTargetName")]
    public ChatMessage? TargetName { get; init; }

    public bool HasTargetName => this.TargetName != null;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(this.Message);
        writer.WriteVarInt(this.ChatType);
        writer.WriteChat(this.SenderName);
        writer.WriteOptional(this.TargetName);
    }
}
