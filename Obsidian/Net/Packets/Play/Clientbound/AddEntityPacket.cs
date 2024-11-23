using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class AddEntityPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public Guid Uuid { get; init; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public EntityType Type { get; init; }

    [Field(3), DataFormat(typeof(double))]
    public VectorF Position { get; init; }

    [Field(4)]
    public Angle Pitch { get; init; }

    [Field(5)]
    public Angle Yaw { get; init; }

    [Field(6)]
    public Angle HeadYaw { get; init; }

    [Field(7), VarLength]
    public int Data { get; init; }

    [Field(8)]
    public Velocity Velocity { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(EntityId);
        writer.WriteUuid(Uuid);
        writer.WriteVarInt(Type);
        writer.WriteAbsolutePositionF(Position);
        writer.WriteByte(Pitch.Value);
        writer.WriteByte(Yaw.Value);
        writer.WriteByte(HeadYaw.Value);
        writer.WriteVarInt(Data);
        writer.WriteVelocity(Velocity);
    }
}
