using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class AddExperienceOrbPacket(short count, VectorF position)
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1), DataFormat(typeof(double))]
    public VectorF Position { get; } = position;

    [Field(2)]
    public short Count { get; } = count;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        writer.WriteAbsolutePositionF(this.Position);
        writer.WriteShort(this.Count);
    }
}
