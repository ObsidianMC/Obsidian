using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDefaultSpawnPositionPacket(VectorF position, Angle yaw, Angle pitch)
{
    [Field(0)]
    public VectorF Position { get; } = position;

    [Field(1), DataFormat(typeof(float))]
    public Angle Yaw { get; set; } = yaw;

    [Field(2), DataFormat(typeof(float))]
    public Angle Pitch { get; } = pitch;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePositionF(this.Position);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);
    }
}
