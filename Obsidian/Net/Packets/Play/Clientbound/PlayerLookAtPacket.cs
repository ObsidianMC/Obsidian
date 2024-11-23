using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlayerLookAtPacket
{
    [Field(0), VarLength, ActualType(typeof(int))]
    public Anchor FromAnchor { get; set; }

    [Field(1), DataFormat(typeof(double))]
    public VectorF Target { get; set; }

    [Field(2), VarLength]
    public int? EntityId { get; set; }

    [Field(4), VarLength, ActualType(typeof(int)), Condition("EntityId.HasValue")]
    public Anchor ToAnchor { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.FromAnchor);
        writer.WriteAbsolutePositionF(this.Target);

        writer.WriteBoolean(this.EntityId.HasValue);

        if (this.EntityId is int entityId)
        {
            writer.WriteVarInt(entityId);
            writer.WriteVarInt(this.ToAnchor);
        }
    }
}

public enum Anchor : int
{
    Feet = 0,
    Eyes
}
