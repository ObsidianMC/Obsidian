using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public sealed partial class DeleteMessagePacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int Length { get; init; }

    [Field(1)]
    public byte[] Signature { get; init; }

    public int Id => 0x1C;
}
