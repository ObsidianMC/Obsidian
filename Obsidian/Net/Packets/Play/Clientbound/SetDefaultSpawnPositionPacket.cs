using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDefaultSpawnPositionPacket(GlobalPos position, Angle yaw, Angle pitch)
{
    [Field(0)]
    public GlobalPos Position { get; } = position;

    [Field(1), DataFormat(typeof(float))]
    public Angle Yaw { get; set; } = yaw;

    [Field(2), DataFormat(typeof(float))]
    public Angle Pitch { get; } = pitch;

    public override void Serialize(INetStreamWriter writer)
    {
        GlobalPos.Write(this.Position, writer);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);
    }
}
