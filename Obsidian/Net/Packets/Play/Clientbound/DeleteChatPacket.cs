using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class DeleteChatPacket
{
    [Field(1)]
    public required ReadOnlyMemory<byte> Signature { get; init; }

    public int? SignatureIndex { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        new ChatMessageTypes.PackedChatSignature(this.SignatureIndex ?? -1, this.Signature).Write(writer);
    }
}
