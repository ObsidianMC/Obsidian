using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class DeleteChatPacket
{
    [Field(0), VarLength]
    public int Length { get; init; }

    [Field(1)]
    public byte[] Signature { get; init; }
}
