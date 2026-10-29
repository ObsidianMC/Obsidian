using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDefaultSpawnPositionPacket(GlobalPos position, float yaw, float pitch)
{
    [Field(0)]
    public GlobalPos Position { get; } = position;

    [Field(1)]
    public float Yaw { get; set; } = yaw;

    [Field(2)]
    public float Pitch { get; } = pitch;

    public override void Serialize(INetStreamWriter writer)
    {
        GlobalPos.Write(this.Position, writer);
        writer.WriteSingle(this.Yaw);
        writer.WriteSingle(this.Pitch);
    }
}
