using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockAction : IClientboundPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    [Field(1)]
    public byte ActionId { get; init; }

    [Field(2)]
    public byte ActionParam { get; init; }

    [Field(3), VarLength]
    public int BlockType { get; init; }

    [Field(4), VarLength]
    public int Sequence { get; init; }//TODO figure this out

    public int Id => 0x0B;
}
