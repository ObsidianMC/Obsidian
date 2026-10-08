using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class HurtAnimationPacket
{
    [Field(0), VarLength]
    public required int EntityId { get; init; }
    [Field(1)]
    public required float Yaw { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(EntityId);
        writer.WriteSingle(Yaw);
    }
}
