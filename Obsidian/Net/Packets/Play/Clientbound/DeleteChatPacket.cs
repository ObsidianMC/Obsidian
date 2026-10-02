using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class DeleteChatPacket
{
    [Field(1)]
    public required ReadOnlyMemory<byte> Signature { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Signature.Length);
        writer.WriteByteArray(this.Signature.Span);
    }
}
