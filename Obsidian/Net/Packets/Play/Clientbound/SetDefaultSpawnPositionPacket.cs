using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDefaultSpawnPositionPacket(VectorF position, Angle angle)
{
    [Field(0)]
    public VectorF Position { get; } = position;

    [Field(1), DataFormat(typeof(float))]
    public Angle Angle { get; set; } = angle;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePositionF(this.Position);
        writer.WriteSingle(this.Angle);
    }
}
