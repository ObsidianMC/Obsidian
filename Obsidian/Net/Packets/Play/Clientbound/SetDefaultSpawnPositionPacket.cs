using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDefaultSpawnPositionPacket(GlobalPosition position, Angle yaw, Angle pitch)
{
    [Field(0)]
    public GlobalPosition Position { get; } = position;

    [Field(1), DataFormat(typeof(float))]
    public Angle Yaw { get; set; } = yaw;

    [Field(2), DataFormat(typeof(float))]
    public Angle Pitch { get; } = pitch;

    public override void Serialize(INetStreamWriter writer)
    {
        GlobalPosition.Write(this.Position, writer);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);
    }
}
