using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockUpdatePacket
{
    [Field(0)]
    public Vector Position { get; }

    [Field(1), VarLength]
    public int BlockId { get; }

    public BlockUpdatePacket(Vector position, int block)
    {
        Position = position;
        BlockId = block;
    }
}
