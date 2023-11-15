using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockUpdatePacket : IClientboundPacket
{
    [Field(0)]
    public Vector Position { get; }

    [Field(1), VarLength]
    public int BlockId { get; }

    public int Id => 0x09;

    public BlockUpdatePacket(Vector position, int block)
    {
        Position = position;
        BlockId = block;
    }
}
