using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockUpdatePacket(Vector position, int block)
{
    [Field(0)]
    public Vector Position { get; } = position;

    [Field(1), VarLength]
    public int BlockId { get; } = block;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(this.Position);
        writer.WriteVarInt(this.BlockId);
    }
}
