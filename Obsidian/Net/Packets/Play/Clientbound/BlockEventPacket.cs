using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BlockEventPacket
{
    [Field(0)]
    public Vector Position { get; init; }

    [Field(1)]
    public byte ActionId { get; init; }

    [Field(2)]
    public byte ActionParam { get; init; }

    [Field(3), VarLength]
    public int BlockType { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(this.Position);
        writer.WriteByte(this.ActionId);
        writer.WriteByte(this.ActionParam);
        writer.WriteVarInt(this.BlockType);
    }
}
